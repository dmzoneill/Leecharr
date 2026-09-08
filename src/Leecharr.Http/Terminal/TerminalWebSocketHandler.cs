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

        if (configFileProvider != null && !configFileProvider.TerminalAccessEnabled)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Terminal process execution is disabled in security configuration.");
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
        ITerminalSession session;
        try
        {
            session = ptyService.CreateSession(cwd, cols, rows);
        }
        catch (Exception ex)
        {
            var errPayload = JsonSerializer.Serialize(new { type = "output", data = $"\r\n\x1b[1;31m[Terminal session error: {ex.Message}]\x1b[0m\r\n" });
            var errBytes = Encoding.UTF8.GetBytes(errPayload);
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket.SendAsync(new ArraySegment<byte>(errBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, ex.Message, CancellationToken.None);
            }

            return;
        }

        await using var sessionDisposer = session;

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
            var decoder = Encoding.UTF8.GetDecoder();
            var buffer = new byte[4096];
            var charBuffer = new char[4096];
            try
            {
                while (!cts.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    int bytesRead = await session.ReadAsync(buffer, cts.Token);
                    if (bytesRead <= 0)
                    {
                        break;
                    }

                    int charCount = decoder.GetChars(buffer, 0, bytesRead, charBuffer, 0, flush: false);
                    if (charCount > 0)
                    {
                        string text = new string(charBuffer, 0, charCount);
                        var payload = JsonSerializer.Serialize(new { type = "output", data = text });
                        await SafeSendTextAsync(payload, cts.Token);
                    }
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
                        bool isHandled = false;
                        try
                        {
                            using var doc = await JsonDocument.ParseAsync(ms, cancellationToken: cts.Token);
                            var root = doc.RootElement;

                            if (root.ValueKind == JsonValueKind.Object)
                            {
                                isHandled = true;

                                if (root.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
                                {
                                    var type = typeProp.GetString();
                                    if (type == "input" && root.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String)
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
                                             root.TryGetProperty("rows", out var rowsProp) &&
                                             colsProp.TryGetInt32(out var cols) &&
                                             rowsProp.TryGetInt32(out var rows))
                                    {
                                        session.Resize(cols, rows);
                                    }
                                    else if (type == "ping")
                                    {
                                        await SafeSendTextAsync("{\"type\":\"pong\"}", cts.Token);
                                    }
                                }
                            }
                        }
                        catch (JsonException)
                        {
                            // Non-JSON input stream fallback
                        }
                        catch (InvalidOperationException)
                        {
                            // Malformed JSON element extraction fallback
                        }

                        if (!isHandled && ms.Length > 0)
                        {
                            var rawBytes = ms.ToArray();
                            await session.WriteAsync(rawBytes, cts.Token);
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
