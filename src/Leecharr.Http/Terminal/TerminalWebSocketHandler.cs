// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Terminal;

public static class TerminalWebSocketHandler
{
    public static async Task HandleWebSocket(HttpContext context, IPtyTerminalService ptyService, IConfigService configService)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket upgrade required.");
            return;
        }

        // Determine working directory
        string requestedCwd = context.Request.Query["cwd"];
        string cwd = null;

        if (!string.IsNullOrWhiteSpace(requestedCwd) && Directory.Exists(requestedCwd))
        {
            cwd = requestedCwd;
        }
        else if (!string.IsNullOrWhiteSpace(configService.DownloadDir) && Directory.Exists(configService.DownloadDir))
        {
            cwd = configService.DownloadDir;
        }
        else
        {
            cwd = Directory.GetCurrentDirectory();
        }

        int cols = int.TryParse(context.Request.Query["cols"], out int c) ? Math.Max(10, c) : 100;
        int rows = int.TryParse(context.Request.Query["rows"], out int r) ? Math.Max(5, r) : 30;

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        await using var session = ptyService.CreateSession(cwd, cols, rows);

        using var cts = new CancellationTokenSource();

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
                    var sendBytes = Encoding.UTF8.GetBytes(payload);

                    await webSocket.SendAsync(
                        new ArraySegment<byte>(sendBytes),
                        WebSocketMessageType.Text,
                        true,
                        cts.Token);
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
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.Count > 0)
                    {
                        string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        using var doc = JsonDocument.Parse(json);
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
                                var pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                                await webSocket.SendAsync(
                                    new ArraySegment<byte>(pong),
                                    WebSocketMessageType.Text,
                                    true,
                                    cts.Token);
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
                var exitMsg = Encoding.UTF8.GetBytes("{\"type\":\"exit\"}");
                await webSocket.SendAsync(
                    new ArraySegment<byte>(exitMsg),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);

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

        session.Kill();
    }
}
