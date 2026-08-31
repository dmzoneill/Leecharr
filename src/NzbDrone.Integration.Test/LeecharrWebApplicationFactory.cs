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
    private readonly WebApplication _app;
    private readonly string _tempDir;
    private bool _disposed;

    public string BaseUrl { get; }

    public string ApiKey { get; private set; } = string.Empty;

    public HttpClient Client { get; }

    public LeecharrWebApplicationFactory()
    {
        _tempDir = Path.Combine(
            Path.GetTempPath(),
            "leecharr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var port = FindFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var startupContext = new StartupContext("--data=" + _tempDir);
        _app = Bootstrap.CreateApplication(startupContext, new[] { BaseUrl });
        _app.StartAsync().GetAwaiter().GetResult();

        LoadApiKey();
        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        if (!string.IsNullOrEmpty(ApiKey))
        {
            Client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        }

        WaitForHealthy();
    }

    private void LoadApiKey()
    {
        try
        {
            var configFile = Path.Combine(_tempDir, "config.xml");
            if (!File.Exists(configFile))
            {
                return;
            }

            using var stream = File.OpenRead(configFile);
            var doc = System.Xml.Linq.XDocument.Load(stream);
            ApiKey = doc.Root?.Element("ApiKey")?.Value ?? string.Empty;
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
                var response = Client.GetAsync("/api/v1/system/status").GetAwaiter().GetResult();
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
            throw new InvalidOperationException($"Test server failed to start at {BaseUrl}");
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Client.Dispose();

        try
        {
            _app.StopAsync().GetAwaiter().GetResult();
            _app.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore during shutdown
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore temp dir deletion failure
        }
    }
}
