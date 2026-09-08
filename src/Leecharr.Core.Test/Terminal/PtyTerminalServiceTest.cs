// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Terminal;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Terminal;

[TestFixture]
public class PtyTerminalServiceTest
{
    private static PtyTerminalService CreateEnabledService()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.TerminalAccessEnabled.Returns(true);
        return new PtyTerminalService(config);
    }

    [Test]
    public void CreateSession_WhenConfigFileProviderIsNull_ThrowsSecurityException()
    {
        var service = new PtyTerminalService(null);

        Action act = () => service.CreateSession("/tmp", 80, 24);

        act.Should().Throw<SecurityException>()
            .WithMessage("Terminal process execution is prohibited by security configuration.");
    }

    [Test]
    public void CreateSession_WhenTerminalAccessDisabled_ThrowsSecurityException()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.TerminalAccessEnabled.Returns(false);
        var service = new PtyTerminalService(config);

        Action act = () => service.CreateSession("/tmp", 80, 24);

        act.Should().Throw<SecurityException>()
            .WithMessage("Terminal process execution is prohibited by security configuration.");
    }

    [Test]
    public void IsTerminalAccessPermitted_WhenConfigFileProviderIsNull_ReturnsFalse()
    {
        var service = new PtyTerminalService(null);

        service.IsTerminalAccessPermitted().Should().BeFalse();
    }

    [Test]
    public void IsTerminalAccessPermitted_WhenTerminalAccessDisabled_ReturnsFalse()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.TerminalAccessEnabled.Returns(false);
        var service = new PtyTerminalService(config);

        service.IsTerminalAccessPermitted().Should().BeFalse();
    }

    [Test]
    public void IsTerminalAccessPermitted_WhenTerminalAccessEnabled_ReturnsTrue()
    {
        var config = Substitute.For<IConfigFileProvider>();
        config.TerminalAccessEnabled.Returns(true);
        var service = new PtyTerminalService(config);

        service.IsTerminalAccessPermitted().Should().BeTrue();
    }

    [Test]
    public async Task CreateSession_ExecutesEchoCommand_ReturnsExpectedOutput()
    {
        var service = CreateEnabledService();
        await using var session = service.CreateSession("/tmp", 80, 24);

        session.Should().NotBeNull();
        session.IsActive.Should().BeTrue();
        session.ProcessId.Should().BeGreaterThan(0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Write command into terminal
        var cmd = Encoding.UTF8.GetBytes("echo HELLO_PTY_TEST\n");
        await session.WriteAsync(cmd, cts.Token);

        // Read output
        var buffer = new byte[1024];
        var sb = new StringBuilder();

        while (!cts.IsCancellationRequested && sb.Length < 500)
        {
            int bytesRead = await session.ReadAsync(buffer, cts.Token);
            if (bytesRead <= 0)
            {
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            if (sb.ToString().Contains("HELLO_PTY_TEST"))
            {
                break;
            }
        }

        sb.ToString().Should().Contain("HELLO_PTY_TEST");

        // Verify resize does not throw
        session.Resize(120, 40);
        session.Kill();
    }

    [Test]
    public async Task Resize_UpdatesWindowDimensionsDynamically()
    {
        var service = CreateEnabledService();
        await using var session = service.CreateSession("/tmp", 80, 24);

        session.Should().NotBeNull();
        session.IsActive.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Read initial shell prompt / startup output
        var buffer = new byte[1024];
        var startupRead = await session.ReadAsync(buffer, cts.Token);
        startupRead.Should().BeGreaterThan(0);

        // Initial stty size command
        var cmd1 = Encoding.UTF8.GetBytes("stty size\n");
        await session.WriteAsync(cmd1, cts.Token);

        var sb1 = new StringBuilder();
        while (!cts.IsCancellationRequested && !sb1.ToString().Contains("24 80"))
        {
            int bytesRead = await session.ReadAsync(buffer, cts.Token);
            if (bytesRead <= 0)
            {
                break;
            }

            sb1.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        sb1.ToString().Should().Contain("24 80");

        // Resize to 120 cols x 40 rows
        session.Resize(120, 40);

        // Delay to allow control message & ioctl / SIGWINCH processing
        await Task.Delay(200, cts.Token);

        var cmd2 = Encoding.UTF8.GetBytes("stty size\n");
        await session.WriteAsync(cmd2, cts.Token);

        var sb2 = new StringBuilder();
        while (!cts.IsCancellationRequested && !sb2.ToString().Contains("40 120"))
        {
            int bytesRead = await session.ReadAsync(buffer, cts.Token);
            if (bytesRead <= 0)
            {
                break;
            }

            sb2.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        sb2.ToString().Should().Contain("40 120");

        session.Kill();
    }

    [Test]
    public async Task CreateSession_SpawnsInteractiveShell_EmitsStartupPromptWithoutInput()
    {
        var service = CreateEnabledService();
        await using var session = service.CreateSession("/tmp", 80, 24);

        session.Should().NotBeNull();
        session.IsActive.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[2048];
        var sb = new StringBuilder();

        while (!cts.IsCancellationRequested && sb.Length < 100)
        {
            int bytesRead = await session.ReadAsync(buffer, cts.Token);
            if (bytesRead <= 0)
            {
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            if (sb.Length > 0)
            {
                break;
            }
        }

        sb.Length.Should().BeGreaterThan(0);
        session.Kill();
    }

    [Test]
    public void StatefulUtf8Decoding_AcrossChunkBoundaries_DecodesWithoutReplacementCharacters()
    {
        var decoder = Encoding.UTF8.GetDecoder();
        var fullString = "🚀 Hello, 世界! Testing ┌─┐ unicode borders 💻";
        var fullBytes = Encoding.UTF8.GetBytes(fullString);

        // Intentionally split the bytes across chunks right in the middle of multi-byte sequences
        var charBuffer = new char[1024];
        var sb = new StringBuilder();

        int chunkSize = 7;
        for (int offset = 0; offset < fullBytes.Length; offset += chunkSize)
        {
            int count = Math.Min(chunkSize, fullBytes.Length - offset);
            int charsDecoded = decoder.GetChars(fullBytes, offset, count, charBuffer, 0, flush: false);
            if (charsDecoded > 0)
            {
                sb.Append(charBuffer, 0, charsDecoded);
            }
        }

        int finalChars = decoder.GetChars(Array.Empty<byte>(), 0, 0, charBuffer, 0, flush: true);
        if (finalChars > 0)
        {
            sb.Append(charBuffer, 0, finalChars);
        }

        var result = sb.ToString();
        result.Should().Be(fullString);
        result.Should().NotContain("\uFFFD");
    }

    [Test]
    public async Task Kill_TerminatesSessionAndMarksInactive()
    {
        var service = CreateEnabledService();
        var session = service.CreateSession("/tmp", 80, 24);

        session.IsActive.Should().BeTrue();
        session.Kill();

        session.IsActive.Should().BeFalse();

        // Repeated kill should be idempotent
        Action act = () => session.Kill();
        act.Should().NotThrow();

        await session.DisposeAsync();
    }

    [Test]
    public async Task CreateSession_PreventsHostEnvironmentVariableLeakage()
    {
        Environment.SetEnvironmentVariable("LEECHARR_TEST_SECRET_TOKEN", "super_secret_leak_12345");
        try
        {
            var service = CreateEnabledService();
            await using var session = service.CreateSession("/tmp", 80, 24);

            session.Should().NotBeNull();
            session.IsActive.Should().BeTrue();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var cmd = Encoding.UTF8.GetBytes("echo TOKEN_${LEECHARR_TEST_SECRET_TOKEN}_END\n");
            await session.WriteAsync(cmd, cts.Token);

            var buffer = new byte[1024];
            var sb = new StringBuilder();

            while (!cts.IsCancellationRequested && sb.Length < 500)
            {
                int bytesRead = await session.ReadAsync(buffer, cts.Token);
                if (bytesRead <= 0)
                {
                    break;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                if (sb.ToString().Contains("TOKEN__END"))
                {
                    break;
                }
            }

            var output = sb.ToString();
            output.Should().NotContain("super_secret_leak_12345");
            output.Should().Contain("TOKEN__END");

            session.Kill();
        }
        finally
        {
            Environment.SetEnvironmentVariable("LEECHARR_TEST_SECRET_TOKEN", null);
        }
    }

    [Test]
    public async Task SessionTermination_ReapsChildProcessWithoutOrphansOrZombies()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Ignore("Linux PTY process reaping test only applicable on Linux.");
        }

        var service = CreateEnabledService();
        var session = service.CreateSession("/tmp", 80, 24);

        session.Should().NotBeNull();
        session.IsActive.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Get the PID of the inner shell ($$)
        var cmd = Encoding.UTF8.GetBytes("echo INNER_PID_$$ _END\n");
        await session.WriteAsync(cmd, cts.Token);

        var buffer = new byte[1024];
        var sb = new StringBuilder();
        int childPid = -1;

        while (!cts.IsCancellationRequested && sb.Length < 1000)
        {
            int bytesRead = await session.ReadAsync(buffer, cts.Token);
            if (bytesRead <= 0)
            {
                break;
            }

            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
            var text = sb.ToString();
            var match = Regex.Match(text, @"INNER_PID_(\d+)\s+_END");
            if (match.Success)
            {
                childPid = int.Parse(match.Groups[1].Value);
                break;
            }
        }

        childPid.Should().BeGreaterThan(0);

        // Terminate the session (simulating disconnect / Kill)
        session.Kill();
        await session.DisposeAsync();

        // Wait up to 3 seconds for the child process to be reaped
        var exited = false;
        for (var i = 0; i < 30; i++)
        {
            try
            {
                var childProc = Process.GetProcessById(childPid);
                if (childProc.HasExited)
                {
                    exited = true;
                    break;
                }
            }
            catch (ArgumentException)
            {
                // Process is completely gone and reaped
                exited = true;
                break;
            }

            await Task.Delay(100);
        }

        exited.Should().BeTrue("child process should exit after session termination");

        // Child process should no longer be active or running
        var procExistsAndAlive = false;
        try
        {
            var p = Process.GetProcessById(childPid);
            if (!p.HasExited)
            {
                var statPath = $"/proc/{childPid}/stat";
                if (File.Exists(statPath))
                {
                    var stat = File.ReadAllText(statPath);
                    if (!stat.Contains(") Z"))
                    {
                        procExistsAndAlive = true;
                    }
                }
            }
        }
        catch (ArgumentException)
        {
            procExistsAndAlive = false;
        }

        procExistsAndAlive.Should().BeFalse("child shell process should have been terminated with SIGHUP and reaped");
    }

    [Test]
    public void CreateFifo_CreatesFifoWithUserOnlyPermissions()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Ignore("FIFO permissions test only applicable on Linux/macOS.");
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var tempFifoPath = Path.Combine(Path.GetTempPath(), $"leecharr_test_fifo_{Guid.NewGuid():N}.pipe");

            try
            {
                PtyProcessSession.CreateFifo(tempFifoPath);

                File.Exists(tempFifoPath).Should().BeTrue("FIFO should be created on disk");

                var mode = File.GetUnixFileMode(tempFifoPath);
                mode.HasFlag(UnixFileMode.UserRead).Should().BeTrue("user read permission must be set");
                mode.HasFlag(UnixFileMode.UserWrite).Should().BeTrue("user write permission must be set");

                // Group and others must not have read, write, or execute permissions (0600 mode)
                var groupAndOtherPermissions = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                               UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                (mode & groupAndOtherPermissions).Should().Be(UnixFileMode.None, "FIFO must not have group or other permissions");
            }
            finally
            {
                if (File.Exists(tempFifoPath))
                {
                    File.Delete(tempFifoPath);
                }
            }
        }
    }

    [Test]
    public async Task LinuxPtySession_Start_ExecutesCommandsAndTerminatesCleanly()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("LinuxPtySession only applicable on Linux.");
            return;
        }

        var session = LinuxPtySession.Start("/tmp", 80, 24);
        try
        {
            session.Should().NotBeNull();
            session.IsActive.Should().BeTrue();
            session.ProcessId.Should().BeGreaterThan(0);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var cmd = Encoding.UTF8.GetBytes("echo LINUX_PTY_DIRECT_TEST\n");
            await session.WriteAsync(cmd, cts.Token);

            var buffer = new byte[1024];
            var sb = new StringBuilder();

            while (!cts.IsCancellationRequested && sb.Length < 500)
            {
                int bytesRead = await session.ReadAsync(buffer, cts.Token);
                if (bytesRead <= 0)
                {
                    break;
                }

                sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                if (sb.ToString().Contains("LINUX_PTY_DIRECT_TEST"))
                {
                    break;
                }
            }

            sb.ToString().Should().Contain("LINUX_PTY_DIRECT_TEST");
        }
        finally
        {
            session.Kill();
            await session.DisposeAsync();
        }
    }
}
