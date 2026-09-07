// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Terminal;
using NUnit.Framework;

namespace Leecharr.Core.Test.Terminal;

[TestFixture]
public class FallbackProcessSessionTest
{
    [Test]
    public async Task ReadAsync_WhenShellProcessExits_DrainsBufferAndReturnsZeroCleanly()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var exitCommand = isWindows
            ? "Write-Output FALLBACK_EOF_TEST; exit\r\n"
            : "echo FALLBACK_EOF_TEST && exit\n";

        await using var session = FallbackProcessSession.Start("/tmp", 80, 24);
        session.Should().NotBeNull();
        session.IsActive.Should().BeTrue();
        session.ProcessId.Should().BeGreaterThan(0);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Write command that outputs a string and immediately exits the shell
        var cmd = Encoding.UTF8.GetBytes(exitCommand);
        await session.WriteAsync(cmd, cts.Token);

        var buffer = new byte[1024];
        var sb = new StringBuilder();
        int bytesRead;

        while ((bytesRead = await session.ReadAsync(buffer, cts.Token)) > 0)
        {
            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        // Must have received the output before process exited
        sb.ToString().Should().Contain("FALLBACK_EOF_TEST");

        // ReadAsync reached EOF (bytesRead == 0) within the timeout without hanging
        bytesRead.Should().Be(0);

        // Subsequent reads must continue to return 0
        int subsequentRead = await session.ReadAsync(buffer, cts.Token);
        subsequentRead.Should().Be(0);
    }

    [Test]
    public async Task Kill_TerminatesSessionAndReadAsyncReturnsZero()
    {
        await using var session = FallbackProcessSession.Start("/tmp", 80, 24);
        session.Should().NotBeNull();
        session.IsActive.Should().BeTrue();

        session.Kill();
        session.IsActive.Should().BeFalse();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var buffer = new byte[1024];
        int bytesRead = await session.ReadAsync(buffer, cts.Token);
        bytesRead.Should().Be(0);
    }

    [Test]
    public async Task WriteAsync_WhenCalledMultipleTimes_ProcessesCommandsSequentially()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var cmd1 = isWindows ? "Write-Output FIRST_MSG\r\n" : "echo FIRST_MSG\n";
        var cmd2 = isWindows ? "Write-Output SECOND_MSG; exit\r\n" : "echo SECOND_MSG && exit\n";

        await using var session = FallbackProcessSession.Start("/tmp", 80, 24);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await session.WriteAsync(Encoding.UTF8.GetBytes(cmd1), cts.Token);
        await session.WriteAsync(Encoding.UTF8.GetBytes(cmd2), cts.Token);

        var buffer = new byte[1024];
        var sb = new StringBuilder();
        int bytesRead;

        while ((bytesRead = await session.ReadAsync(buffer, cts.Token)) > 0)
        {
            sb.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
        }

        var output = sb.ToString();
        output.Should().Contain("FIRST_MSG");
        output.Should().Contain("SECOND_MSG");
        bytesRead.Should().Be(0);
    }
}
