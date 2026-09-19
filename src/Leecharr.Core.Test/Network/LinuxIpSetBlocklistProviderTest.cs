// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class LinuxIpSetBlocklistProviderTest
{
    private IDiskProvider diskProvider;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.diskProvider.FileExists(Arg.Any<string>()).Returns(true);
    }

    [Test]
    public async Task ExecuteIpSetCommandAsync_WithLargeStdinAndLargeStderr_DoesNotDeadlock()
    {
        // Python helper script writes > 64 KB to stderr before reading stdin.
        // Under synchronous pipe I/O, this deadlocks on Linux because stderr pipe buffer (64 KB) fills.
        var harness = new ExecutableHarnessProvider(
            this.diskProvider,
            "/usr/bin/python3",
            argumentsPrefix: "-c \"import sys; sys.stderr.write('E' * 131072); sys.stderr.flush(); data = sys.stdin.read(); sys.stdout.write(f'read {len(data)}'); sys.stdout.flush()\"");

        var largeStdin = new string('A', 256 * 1024); // 256 KB stdin

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var (exitCode, stdout, stderr) = await harness.RunCommandAsync(string.Empty, largeStdin, cts.Token);

        exitCode.Should().Be(0);
        stdout.Should().Be($"read {largeStdin.Length}");
        stderr.Length.Should().Be(131072);
    }

    [Test]
    public async Task ExecuteIpSetCommandAsync_WhenCancelled_TerminatesProcessPromptly()
    {
        var harness = new ExecutableHarnessProvider(
            this.diskProvider,
            "/usr/bin/python3",
            argumentsPrefix: "-c \"import time; time.sleep(30)\"");

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(150));

        var startTime = DateTime.UtcNow;
        var act = async () => await harness.RunCommandAsync(string.Empty, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var elapsed = DateTime.UtcNow - startTime;
        elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void ClearRules_FlushesSequentially_WithoutConcurrentNetfilterLockRace()
    {
        var concurrentExecutions = 0;
        var maxConcurrent = 0;
        var executedCommands = new List<string>();

        var mockProvider = new InterceptedLinuxIpSetBlocklistProvider(
            this.diskProvider,
            async (args, stdIn, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref concurrentExecutions);
                lock (executedCommands)
                {
                    if (current > maxConcurrent)
                    {
                        maxConcurrent = current;
                    }

                    executedCommands.Add(args);
                }

                await Task.Delay(50, cancellationToken);
                Interlocked.Decrement(ref concurrentExecutions);
                return (0, string.Empty, string.Empty);
            });

        mockProvider.ClearRules();

        maxConcurrent.Should().Be(1, "ipset commands must execute sequentially to avoid netfilter lock contention");
        executedCommands.Should().ContainInOrder("flush leecharr_blocklist_v4", "flush leecharr_blocklist_v6");
        mockProvider.RuleCount.Should().Be(0);
        mockProvider.IsKernelOffloadActive.Should().BeFalse();
    }

    [Test]
    public async Task ExecuteIpSetCommandAsync_WhenBinaryNotFound_ReturnsNegativeExitCode()
    {
        var missingDisk = Substitute.For<IDiskProvider>();
        missingDisk.FileExists(Arg.Any<string>()).Returns(false);

        var provider = new ExposedLinuxIpSetBlocklistProvider(missingDisk);
        var (exitCode, stdout, stderr) = await provider.RunCommandAsync("list -n");

        exitCode.Should().Be(-1);
        stdout.Should().BeEmpty();
        stderr.Should().Contain("ipset binary not found");
    }

    [Test]
    public async Task LoadRulesAsync_WithLargeRuleSet_BuildsRestoreScriptAndAppliesSuccessfully()
    {
        string capturedScript = null;
        var mockProvider = new InterceptedLinuxIpSetBlocklistProvider(
            this.diskProvider,
            (args, stdIn, cancellationToken) =>
            {
                if (args == "restore")
                {
                    capturedScript = stdIn;
                }

                return Task.FromResult((0, string.Empty, string.Empty));
            });

        var rules = new List<string>();
        for (var i = 1; i <= 2000; i++)
        {
            var b1 = (i >> 8) & 0xFF;
            var b2 = i & 0xFF;
            rules.Add($"10.{b1}.{b2}.0/24");
        }

        var loaded = await mockProvider.LoadRulesAsync(rules);
        loaded.Should().Be(2000);
        mockProvider.IsKernelOffloadActive.Should().BeTrue();

        capturedScript.Should().NotBeNull();
        capturedScript.Length.Should().BeGreaterThan(64 * 1024, "2000 rules script should exceed 64 KB pipe buffer");
        capturedScript.Should().Contain("create leecharr_tmp_v4 hash:net family inet maxelem 1000000 -exist");
        capturedScript.Should().Contain("add leecharr_tmp_v4 10.0.1.0/24 -exist");
        capturedScript.Should().Contain("swap leecharr_tmp_v4 leecharr_blocklist_v4");
    }

    private class ExecutableHarnessProvider : LinuxIpSetBlocklistProvider
    {
        private readonly string binaryPath;
        private readonly string argumentsPrefix;

        public ExecutableHarnessProvider(IDiskProvider diskProvider, string binaryPath, string argumentsPrefix)
            : base(diskProvider)
        {
            this.binaryPath = binaryPath;
            this.argumentsPrefix = argumentsPrefix;
        }

        protected override string GetIpSetBinaryPath()
        {
            return this.binaryPath;
        }

        public Task<(int ExitCode, string StdOut, string StdErr)> RunCommandAsync(
            string arguments,
            string stdIn = null,
            CancellationToken cancellationToken = default)
        {
            var fullArgs = string.IsNullOrWhiteSpace(arguments)
                ? this.argumentsPrefix
                : $"{this.argumentsPrefix} {arguments}";

            return this.ExecuteIpSetCommandAsync(fullArgs, stdIn, cancellationToken);
        }
    }

    private class ExposedLinuxIpSetBlocklistProvider : LinuxIpSetBlocklistProvider
    {
        public ExposedLinuxIpSetBlocklistProvider(IDiskProvider diskProvider)
            : base(diskProvider)
        {
        }

        public Task<(int ExitCode, string StdOut, string StdErr)> RunCommandAsync(
            string arguments,
            string stdIn = null,
            CancellationToken cancellationToken = default)
        {
            return this.ExecuteIpSetCommandAsync(arguments, stdIn, cancellationToken);
        }
    }

    private class InterceptedLinuxIpSetBlocklistProvider : LinuxIpSetBlocklistProvider
    {
        private readonly Func<string, string, CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>> executor;

        public InterceptedLinuxIpSetBlocklistProvider(
            IDiskProvider diskProvider,
            Func<string, string, CancellationToken, Task<(int ExitCode, string StdOut, string StdErr)>> executor)
            : base(diskProvider)
        {
            this.executor = executor;
        }

        protected override Task<(int ExitCode, string StdOut, string StdErr)> ExecuteIpSetCommandAsync(
            string arguments,
            string stdIn = null,
            CancellationToken cancellationToken = default)
        {
            return this.executor(arguments, stdIn, cancellationToken);
        }
    }
}
