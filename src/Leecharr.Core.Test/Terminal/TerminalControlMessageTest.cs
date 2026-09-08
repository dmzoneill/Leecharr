// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Http.Terminal;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Terminal;

[TestFixture]
public class TerminalControlMessageTest
{
    private IConfigFileProvider configFileProvider = null!;
    private IConfigService configService = null!;
    private IPtyTerminalService ptyService = null!;
    private ITerminalSession session = null!;

    [SetUp]
    public void SetUp()
    {
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configFileProvider.AuthenticationEnabled.Returns(false);
        this.configFileProvider.TerminalAccessEnabled.Returns(true);

        this.configService = Substitute.For<IConfigService>();
        this.configService.DownloadDir.Returns("/tmp");

        this.session = Substitute.For<ITerminalSession>();
        this.session.IsActive.Returns(true);
        this.session.ReadAsync(Arg.Any<Memory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ValueTask<int>(Task.Delay(Timeout.Infinite, callInfo.Arg<CancellationToken>()).ContinueWith(_ => 0, TaskScheduler.Default)));

        this.ptyService = Substitute.For<IPtyTerminalService>();
        this.ptyService.CreateSession(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(this.session);
    }

    [Test]
    public void TerminalEnvironmentSanitizer_RemovesSecretEnvironmentVariables()
    {
        var startInfo = new ProcessStartInfo();
        startInfo.Environment["MY_SECRET_API_KEY"] = "super_secret_value_123";
        startInfo.Environment["DATABASE_PASSWORD"] = "password123";
        startInfo.Environment["AWS_SECRET_ACCESS_KEY"] = "aws_secret";

        TerminalEnvironmentSanitizer.Sanitize(startInfo);

        startInfo.Environment.ContainsKey("MY_SECRET_API_KEY").Should().BeFalse();
        startInfo.Environment.ContainsKey("DATABASE_PASSWORD").Should().BeFalse();
        startInfo.Environment.ContainsKey("AWS_SECRET_ACCESS_KEY").Should().BeFalse();

        startInfo.Environment.ContainsKey("TERM").Should().BeTrue();
        startInfo.Environment["TERM"].Should().Be("xterm-256color");
        startInfo.Environment.ContainsKey("COLORTERM").Should().BeTrue();
        startInfo.Environment["COLORTERM"].Should().Be("truecolor");
        startInfo.Environment.ContainsKey("LANG").Should().BeTrue();
        startInfo.Environment.ContainsKey("PS1").Should().BeTrue();
    }

    [Test]
    public async Task HandleWebSocket_WhenMalformedResizeControlMessageSent_DoesNotExecuteOnStdin()
    {
        var fakeWs = new FakeWebSocket();
        fakeWs.EnqueueMessage("{\"type\":\"resize\",\"cols\":\"invalid_cols\",\"rows\":\"invalid_rows\"}");

        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Response.Body = new MemoryStream();
        context.SetFakeWebSocketManager(new FakeWebSocketManager(fakeWs));

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        this.session.DidNotReceive().Resize(Arg.Any<int>(), Arg.Any<int>());
        await this.session.DidNotReceive().WriteAsync(
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()).Contains("resize")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleWebSocket_WhenValidResizeControlMessageSent_InvokesResizeAndDoesNotWriteToStdin()
    {
        var fakeWs = new FakeWebSocket();
        fakeWs.EnqueueMessage("{\"type\":\"resize\",\"cols\":120,\"rows\":40}");

        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Response.Body = new MemoryStream();
        context.SetFakeWebSocketManager(new FakeWebSocketManager(fakeWs));

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        this.session.Received(1).Resize(120, 40);
        await this.session.DidNotReceive().WriteAsync(
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()).Contains("resize")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleWebSocket_WhenInputJsonControlMessageSent_WritesExtractedDataToSessionStdin()
    {
        var fakeWs = new FakeWebSocket();
        fakeWs.EnqueueMessage("{\"type\":\"input\",\"data\":\"echo hello\\n\"}");

        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Response.Body = new MemoryStream();
        context.SetFakeWebSocketManager(new FakeWebSocketManager(fakeWs));

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        await this.session.Received(1).WriteAsync(
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()) == "echo hello\n"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleWebSocket_WhenRawNonJsonInputSent_FallsBackToRawSessionStdinWrite()
    {
        var fakeWs = new FakeWebSocket();
        fakeWs.EnqueueMessage("echo raw_keystroke\n");

        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Response.Body = new MemoryStream();
        context.SetFakeWebSocketManager(new FakeWebSocketManager(fakeWs));

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        await this.session.Received(1).WriteAsync(
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()) == "echo raw_keystroke\n"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task HandleWebSocket_WhenUnknownJsonControlMessageSent_DoesNotExecuteOnStdin()
    {
        var fakeWs = new FakeWebSocket();
        fakeWs.EnqueueMessage("{\"type\":\"custom_action\",\"payload\":\"do_something\"}");

        var context = new DefaultHttpContext();
        context.Request.Path = "/ws/terminal";
        context.Response.Body = new MemoryStream();
        context.SetFakeWebSocketManager(new FakeWebSocketManager(fakeWs));

        await TerminalWebSocketHandler.HandleWebSocket(context, this.ptyService, this.configService, this.configFileProvider);

        await this.session.DidNotReceive().WriteAsync(
            Arg.Is<ReadOnlyMemory<byte>>(m => Encoding.UTF8.GetString(m.ToArray()).Contains("custom_action")),
            Arg.Any<CancellationToken>());
    }

    private sealed class FakeWebSocket : WebSocket
    {
        private readonly Queue<byte[]> incomingMessages = new();
        private WebSocketState state = WebSocketState.Open;

        public void EnqueueMessage(string text) => this.incomingMessages.Enqueue(Encoding.UTF8.GetBytes(text));

        public override WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;

        public override string CloseStatusDescription => "Normal";

        public override WebSocketState State => this.state;

        public override string SubProtocol => string.Empty;

        public override void Abort() => this.state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            this.state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            this.state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            if (this.incomingMessages.Count == 0)
            {
                this.state = WebSocketState.CloseReceived;
                return Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "Closed"));
            }

            var msg = this.incomingMessages.Dequeue();
            Buffer.BlockCopy(msg, 0, buffer.Array!, buffer.Offset, msg.Length);
            return Task.FromResult(new WebSocketReceiveResult(msg.Length, WebSocketMessageType.Text, true));
        }

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWebSocketManager : WebSocketManager
    {
        private readonly WebSocket socket;

        public FakeWebSocketManager(WebSocket socket)
        {
            this.socket = socket;
        }

        public override bool IsWebSocketRequest => true;

        public override IList<string> WebSocketRequestedProtocols => new List<string>();

        public override Task<WebSocket> AcceptWebSocketAsync(string subProtocol) => Task.FromResult(this.socket);
    }
}

internal static class HttpContextWebSocketExtensions
{
    public static void SetFakeWebSocketManager(this HttpContext context, WebSocketManager manager)
    {
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature>(new FakeWebSocketFeature(manager));
    }

    private sealed class FakeWebSocketFeature : Microsoft.AspNetCore.Http.Features.IHttpWebSocketFeature
    {
        private readonly WebSocketManager manager;

        public FakeWebSocketFeature(WebSocketManager manager)
        {
            this.manager = manager;
        }

        public bool IsWebSocketRequest => this.manager.IsWebSocketRequest;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => this.manager.AcceptWebSocketAsync(context?.SubProtocol);
    }
}
