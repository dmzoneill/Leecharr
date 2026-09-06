// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Http;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Terminal;

public static class TerminalWebSocketHandler
{
    public static bool IsAuthorized(HttpContext context, IConfigFileProvider configFileProvider)
    {
        if (configFileProvider == null || !configFileProvider.AuthenticationEnabled)
        {
            return true;
        }

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        return RpcAuthenticationHelper.IsAuthenticated(context, configFileProvider);
    }

    public static async Task HandleWebSocket(
        HttpContext context,
        IPtyTerminalService ptyService,
        IConfigService configService,
        IConfigFileProvider configFileProvider = null)
    {
        configFileProvider ??= context.RequestServices?.GetService(typeof(IConfigFileProvider)) as IConfigFileProvider;

        if (!IsAuthorized(context, configFileProvider))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Authentication required for terminal access.");
            await context.Response.CompleteAsync();
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.");
            return;
        }

        // Determine working directory
        string requestedCwd = context.Request.Query["cwd"];
        string cwd = null;

        if (!string.IsNullOrWhiteSpace(requestedCwd))
        {
            var cleaned = new string(requestedCwd.Where(ch => !char.IsControl(ch) && ch != '\x1b').ToArray()).Trim();
            if (!string.IsNullOrWhiteSpace(cleaned) && Directory.Exists(cleaned))
            {
                cwd = cleaned;
            }
        }

        if (cwd == null)
        {
            if (!string.IsNullOrWhiteSpace(configService.DownloadDir) && Directory.Exists(configService.DownloadDir))
            {
                cwd = configService.DownloadDir;
            }
            else
            {
                cwd = Directory.GetCurrentDirectory();
            }
        }

        int cols = int.TryParse(context.Request.Query["cols"], out int c) ? Math.Max(10, c) : 100;
        int rows = int.TryParse(context.Request.Query["rows"], out int r) ? Math.Max(5, r) : 30;

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await using var session = ptyService.CreateSession(cwd, cols, rows);

        using var cts = new CancellationTokenSource();
        var sendLock = new SemaphoreSlim(1, 1);

        async Task SafeSendTextAsync(string text, CancellationToken ct)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        ct).ConfigureAwait(false);
                }
            }
            finally
            {
                sendLock.Release();
            }
        }

        var readPtyTask = Task.Run(async () =>
        {
            var buffer = new byte[4096];
            try
            {
                while (!cts.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    int bytesRead = await session.ReadAsync(buffer, cts.Token);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    string text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var payload = JsonSerializer.Serialize(new { type = "output", data = text });
                    await SafeSendTextAsync(payload, cts.Token);
                }
            }
            catch
            {
                // Normal exit on disconnect / stream closed
            }
            finally
            {
                cts.Cancel();
            }
        });

        var receiveWsTask = Task.Run(async () =>
        {
            var buffer = new byte[8192];
            try
            {
                while (!cts.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (ms.Length > 0)
                    {
                        ms.Seek(0, SeekOrigin.Begin);
                        using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: cts.Token);
                        var root = doc.RootElement;

                        if (root.TryGetProperty("type", out var typeProp))
                        {
                            var type = typeProp.GetString();
                            if (type == "input" && root.TryGetProperty("data", out var dataProp))
                            {
                                var inputStr = dataProp.GetString();
                                if (!string.IsNullOrEmpty(inputStr))
                                {
                                    var inputBytes = Encoding.UTF8.GetBytes(inputStr);
                                    await session.WriteAsync(inputBytes, cts.Token);
                                }
                            }
                            else if (type == "resize" &&
                                     root.TryGetProperty("cols", out var colsProp) &&
                                     root.TryGetProperty("rows", out var rowsProp))
                            {
                                session.Resize(colsProp.GetInt32(), rowsProp.GetInt32());
                            }
                            else if (type == "ping")
                            {
                                await SafeSendTextAsync("{\"type\":\"pong\"}", cts.Token);
                            }
                        }
                    }
                }
            }
            catch
            {
                // Normal disconnect
            }
            finally
            {
                cts.Cancel();
            }
        });

        await Task.WhenAny(readPtyTask, receiveWsTask);
        cts.Cancel();

        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await SafeSendTextAsync("{\"type\":\"exit\"}", CancellationToken.None);

                await webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Terminal session terminated",
                    CancellationToken.None);
            }
        }
        catch
        {
            // Ignored on socket close
        }
        finally
        {
            sendLock.Dispose();
        }

        session.Kill();
    }
}
