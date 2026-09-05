// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Terminal;
using NUnit.Framework;

namespace Leecharr.Core.Test.Terminal;

[TestFixture]
public class PtyTerminalServiceTest
{
    [Test]
    public async Task CreateSession_ExecutesEchoCommand_ReturnsExpectedOutput()
    {
        var service = new PtyTerminalService();
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
        var service = new PtyTerminalService();
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
}
