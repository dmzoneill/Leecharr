// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent.Tracker;

public class HttpTrackerService : IHttpTrackerService
{
    private readonly IEmbeddedTrackerHttpProcessor httpProcessor;
    private readonly IConfigService configService;
    private readonly IConfigFileProvider configFileProvider;
    private readonly object syncRoot = new();
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private HttpListener listener;
    private CancellationTokenSource cts;
    private Task listenTask;
    private int disposed;

    public HttpTrackerService(
        IEmbeddedTrackerHttpProcessor httpProcessor,
        IConfigService configService = null,
        IConfigFileProvider configFileProvider = null)
    {
        this.httpProcessor = httpProcessor;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
    }

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (this.syncRoot)
        {
            if (this.IsRunning)
            {
                return Task.CompletedTask;
            }

            if (!TrackerHttpEndpointPolicy.ShouldRunDedicatedListener(this.configService, this.configFileProvider))
            {
                return Task.CompletedTask;
            }

            var port = this.configService.TrackerHttpPort;
            var bindAddress = TrackerBindAddressHelper.ResolveBindAddress(this.configService?.TrackerBindAddress);
            var prefix = BuildListenerPrefix(bindAddress, port);

            try
            {
                this.listener = new HttpListener();
                this.listener.Prefixes.Add(prefix);
                this.listener.Start();
                this.Port = port;
                this.cts = new CancellationTokenSource();
                this.listenTask = Task.Run(() => this.ListenLoopAsync(this.cts.Token), CancellationToken.None);
                this.IsRunning = true;
                this.logger.Info("HTTP Tracker server started on {0}", prefix.TrimEnd('/'));
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Failed to start HTTP Tracker server on {0}", prefix.TrimEnd('/'));
                this.CleanupListener();
                throw;
            }

            return Task.CompletedTask;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource tokenSource;
        Task task;

        lock (this.syncRoot)
        {
            if (!this.IsRunning)
            {
                return;
            }

            this.IsRunning = false;
            tokenSource = this.cts;
            task = this.listenTask;
            this.CleanupListener();
        }

        if (tokenSource != null)
        {
            try
            {
                await tokenSource.CancelAsync();
            }
            catch (ObjectDisposedException ex)
            {
                this.logger.Trace(ex, "Token source already disposed during HTTP tracker shutdown");
            }
        }

        if (task != null)
        {
            try
            {
                await Task.WhenAny(task, Task.Delay(2000, cancellationToken));
            }
            catch (OperationCanceledException ex)
            {
                this.logger.Trace(ex, "Shutdown delay or task wait was cancelled");
            }
        }

        tokenSource?.Dispose();
        this.logger.Info("HTTP Tracker server stopped");
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await this.StopAsync(cancellationToken);
        await this.StartAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) == 1)
        {
            return;
        }

        _ = this.StopAsync();
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context = null;
            try
            {
                context = await this.listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException ex) when (cancellationToken.IsCancellationRequested)
            {
                this.logger.Trace(ex, "HTTP tracker listener stopped");
                break;
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "HTTP tracker listener error");
                continue;
            }

            if (context != null)
            {
                _ = Task.Run(() => this.HandleRequest(context), CancellationToken.None);
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            var localIp = request.LocalEndPoint?.Address;
            var remoteIp = request.RemoteEndPoint?.Address;
            var httpRequest = new EmbeddedTrackerHttpRequest
            {
                RawQuery = request.Url?.Query,
                RemoteIp = remoteIp,
                LocalIp = localIp,
                ForwardedFor = request.Headers["X-Forwarded-For"],
                RealIp = request.Headers["X-Real-IP"],
            };

            EmbeddedTrackerHttpResponse result;
            var path = request.Url?.AbsolutePath ?? string.Empty;
            if (string.Equals(path, "/announce", StringComparison.OrdinalIgnoreCase))
            {
                result = this.httpProcessor.ProcessAnnounce(httpRequest);
            }
            else if (string.Equals(path, "/scrape", StringComparison.OrdinalIgnoreCase))
            {
                result = this.httpProcessor.ProcessScrape(httpRequest);
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            if (result.IsNotFound)
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            var body = result.Body ?? Array.Empty<byte>();
            response.StatusCode = 200;
            response.ContentType = result.ContentType ?? "text/plain";
            response.ContentLength64 = body.Length;
            if (body.Length > 0)
            {
                response.OutputStream.Write(body, 0, body.Length);
            }

            response.Close();
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Failed to handle HTTP tracker request");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception closeEx)
            {
                this.logger.Trace(closeEx, "Failed to close HTTP tracker error response");
            }
        }
    }

    private void CleanupListener()
    {
        if (this.listener == null)
        {
            return;
        }

        try
        {
            if (this.listener.IsListening)
            {
                this.listener.Stop();
            }
        }
        catch (Exception ex)
        {
            this.logger.Trace(ex, "Error stopping HTTP tracker listener");
        }
        finally
        {
            this.listener.Close();
            this.listener = null;
        }
    }

    private static string BuildListenerPrefix(IPAddress bindAddress, int port)
    {
        if (TrackerBindAddressHelper.BindsAllInterfaces(bindAddress))
        {
            return $"http://+:{port}/";
        }

        if (bindAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return $"http://[{bindAddress}]:{port}/";
        }

        return $"http://{bindAddress}:{port}/";
    }
}
