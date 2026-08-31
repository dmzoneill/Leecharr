// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Host;

namespace NzbDrone.Integration.Test;

public sealed class LeecharrWebApplicationFactory : IDisposable
{
    private readonly WebApplication app;
    private readonly string tempDir;
    private bool disposed;

    public string BaseUrl { get; }

    public string ApiKey { get; private set; } = string.Empty;

    public HttpClient Client { get; }

    public IServiceProvider Services => this.app.Services;

    public LeecharrWebApplicationFactory()
    {
        this.tempDir = Path.Combine(
            Path.GetTempPath(),
            "leecharr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);

        var port = FindFreePort();
        this.BaseUrl = $"http://127.0.0.1:{port}";

        var startupContext = new StartupContext("--data=" + this.tempDir);
        this.app = Bootstrap.CreateApplication(startupContext, new[] { this.BaseUrl });
        this.app.StartAsync().GetAwaiter().GetResult();

        this.LoadApiKey();
        this.Client = new HttpClient { BaseAddress = new Uri(this.BaseUrl) };
        if (!string.IsNullOrEmpty(this.ApiKey))
        {
            this.Client.DefaultRequestHeaders.Add("X-Api-Key", this.ApiKey);
        }

        this.WaitForHealthy();
    }

    private void LoadApiKey()
    {
        try
        {
            var configFile = Path.Combine(this.tempDir, "config.xml");
            if (!File.Exists(configFile))
            {
                return;
            }

            using var stream = File.OpenRead(configFile);
            var doc = System.Xml.Linq.XDocument.Load(stream);
            this.ApiKey = doc.Root?.Element("ApiKey")?.Value ?? string.Empty;
        }
        catch
        {
            // Key stays empty
        }
    }

    private void WaitForHealthy()
    {
        var healthy = false;
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var response = this.Client.GetAsync("/api/v1/system/status").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    healthy = true;
                    break;
                }
            }
            catch
            {
                // Not ready yet
            }

            Thread.Sleep(100);
        }

        if (!healthy)
        {
            throw new InvalidOperationException($"Test server failed to start at {this.BaseUrl}");
        }
    }

    private static int FindFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Client.Dispose();

        try
        {
            this.app.StopAsync().GetAwaiter().GetResult();
            this.app.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore during shutdown
        }

        try
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, true);
            }
        }
        catch
        {
            // Ignore temp dir deletion failure
        }
    }
}
