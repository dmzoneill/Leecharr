// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Integration.Test;

public sealed class MockArrServer : IDisposable
{
    private readonly HttpListener listener;
    private readonly CancellationTokenSource cts = new();
    private readonly Task listenTask;
    private int nextNotificationId = 1;
    private int nextDownloadClientId = 1;

    public string Url { get; }

    public List<Dictionary<string, object>> Notifications { get; } = new();

    public List<Dictionary<string, object>> DownloadClients { get; } = new();

    public List<string> ReceivedRequests { get; } = new();

    public MockArrServer()
    {
        var tcp = new TcpListener(IPAddress.Loopback, 0);
        tcp.Start();
        var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
        tcp.Stop();

        this.Url = $"http://127.0.0.1:{port}";
        this.listener = new HttpListener();
        this.listener.Prefixes.Add($"{this.Url}/");
        this.listener.Start();

        this.listenTask = Task.Run(this.ListenLoop);
    }

    public void AddExistingNotification(string name, string webhookUrl)
    {
        lock (this.Notifications)
        {
            var id = this.nextNotificationId++;
            this.Notifications.Add(new Dictionary<string, object>
            {
                ["id"] = id,
                ["name"] = name,
                ["implementation"] = "Webhook",
                ["configContract"] = "WebhookSettings",
                ["fields"] = new List<object>
                {
                    new Dictionary<string, object> { ["name"] = "url", ["value"] = webhookUrl },
                    new Dictionary<string, object> { ["name"] = "method", ["value"] = 1 }
                }
            });
        }
    }

    private async Task ListenLoop()
    {
        while (!this.cts.IsCancellationRequested)
        {
            try
            {
                var context = await this.listener.GetContextAsync();
                _ = Task.Run(() => this.HandleRequest(context));
            }
            catch when (this.cts.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                break;
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var res = context.Response;
        var method = req.HttpMethod;
        var path = req.Url?.AbsolutePath ?? string.Empty;

        lock (this.ReceivedRequests)
        {
            this.ReceivedRequests.Add($"{method} {path}");
        }

        var body = string.Empty;
        if (req.HasEntityBody)
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            body = reader.ReadToEnd();
        }

        try
        {
            // Health / System Status
            if (path.EndsWith("/system/status", StringComparison.OrdinalIgnoreCase))
            {
                this.WriteJson(res, HttpStatusCode.OK, new { version = "4.0.0", appName = "Sonarr" });
                return;
            }

            // Notifications
            if (path.EndsWith("/notification", StringComparison.OrdinalIgnoreCase))
            {
                if (method == "GET")
                {
                    lock (this.Notifications)
                    {
                        this.WriteJson(res, HttpStatusCode.OK, this.Notifications);
                    }

                    return;
                }

                if (method == "POST")
                {
                    lock (this.Notifications)
                    {
                        var notif = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();
                        var id = this.nextNotificationId++;
                        notif["id"] = id;
                        this.Notifications.Add(notif);
                        this.WriteJson(res, HttpStatusCode.Created, notif);
                    }

                    return;
                }
            }

            if (path.Contains("/notification/", StringComparison.OrdinalIgnoreCase))
            {
                var idStr = path.Substring(path.LastIndexOf('/') + 1);
                if (int.TryParse(idStr, out var id))
                {
                    if (method == "PUT")
                    {
                        lock (this.Notifications)
                        {
                            var updated = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();
                            updated["id"] = id;
                            var idx = this.Notifications.FindIndex(n => Convert.ToInt32(n["id"].ToString()) == id);
                            if (idx >= 0)
                            {
                                this.Notifications[idx] = updated;
                            }
                            else
                            {
                                this.Notifications.Add(updated);
                            }

                            this.WriteJson(res, HttpStatusCode.OK, updated);
                        }

                        return;
                    }

                    if (method == "DELETE")
                    {
                        lock (this.Notifications)
                        {
                            this.Notifications.RemoveAll(n => Convert.ToInt32(n["id"].ToString()) == id);
                        }

                        res.StatusCode = (int)HttpStatusCode.OK;
                        res.Close();
                        return;
                    }
                }
            }

            // Download client (for Leecharr automatic add)
            if (path.EndsWith("/downloadclient", StringComparison.OrdinalIgnoreCase))
            {
                if (method == "GET")
                {
                    lock (this.DownloadClients)
                    {
                        this.WriteJson(res, HttpStatusCode.OK, this.DownloadClients);
                    }

                    return;
                }

                if (method == "POST")
                {
                    lock (this.DownloadClients)
                    {
                        var dc = JsonSerializer.Deserialize<Dictionary<string, object>>(body) ?? new();
                        var id = this.nextDownloadClientId++;
                        dc["id"] = id;
                        this.DownloadClients.Add(dc);
                        this.WriteJson(res, HttpStatusCode.Created, dc);
                    }

                    return;
                }
            }

            if (path.Contains("/downloadclient/", StringComparison.OrdinalIgnoreCase) && method == "DELETE")
            {
                var idStr = path.Substring(path.LastIndexOf('/') + 1);
                if (int.TryParse(idStr, out var id))
                {
                    lock (this.DownloadClients)
                    {
                        this.DownloadClients.RemoveAll(d => Convert.ToInt32(d["id"].ToString()) == id);
                    }
                }

                res.StatusCode = (int)HttpStatusCode.OK;
                res.Close();
                return;
            }

            // Default fallback
            res.StatusCode = (int)HttpStatusCode.OK;
            res.Close();
        }
        catch
        {
            res.StatusCode = (int)HttpStatusCode.InternalServerError;
            res.Close();
        }
    }

    private void WriteJson(HttpListenerResponse res, HttpStatusCode status, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        res.StatusCode = (int)status;
        res.ContentType = "application/json";
        res.ContentLength64 = bytes.Length;
        res.OutputStream.Write(bytes, 0, bytes.Length);
        res.OutputStream.Flush();
        res.Close();
    }

    public void Dispose()
    {
        this.cts.Cancel();
        try
        {
            this.listener.Stop();
            this.listener.Close();
        }
        catch
        {
        }

        this.cts.Dispose();
    }
}
