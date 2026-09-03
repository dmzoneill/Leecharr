// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class ProcessStreamDrainingTest
{
    [Test]
    public async Task ProcessDraining_WithLargeOutputExceedingPipeBuffer_DoesNotDeadlock()
    {
        // 128 KB output exceeds standard 64 KB Linux pipe buffer
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c \"dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\000' 'A'\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0);
        stdout.Length.Should().Be(128 * 1024);
        stderr.Should().BeEmpty();
    }

    [Test]
    public async Task ProcessDraining_WithLargeStderrExceedingPipeBuffer_DoesNotDeadlock()
    {
        // 128 KB stderr exceeds standard 64 KB Linux pipe buffer
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c \"dd if=/dev/zero bs=1024 count=128 2>/dev/null | tr '\\000' 'B' >&2\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
        await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(cts.Token));

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        process.ExitCode.Should().Be(0);
        stdout.Should().BeEmpty();
        stderr.Length.Should().Be(128 * 1024);
    }
}
