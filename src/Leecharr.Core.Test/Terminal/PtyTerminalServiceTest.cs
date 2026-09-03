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
}
