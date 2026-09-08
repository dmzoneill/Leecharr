// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Http.Transport;

public class DynamicHttpTransportProxy : IHttpTransportEngine, IHttpTransportManager, IHandle<ConfigSavedEvent>, IDisposable
{
    private readonly IEnumerable<IHttpTransportProvider> availableProviders;
    private readonly IConfigService configService;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;
    private readonly SemaphoreSlim switchLock = new(1, 1);
    private IHttpTransportProvider activeProvider;
    private bool disposed;

    public IHttpTransportProvider ActiveProvider => Volatile.Read(ref this.activeProvider);

    public string ActiveProviderId => Volatile.Read(ref this.activeProvider)?.ProviderId ?? "SocketsHttpHandler";

    public DynamicHttpTransportProxy(
        IEnumerable<IHttpTransportProvider> availableProviders,
        IConfigService configService,
        IEventAggregator eventAggregator)
    {
        this.availableProviders = availableProviders ?? Enumerable.Empty<IHttpTransportProvider>();
        this.configService = configService;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var desiredProviderId = this.configService?.ActiveHttpTransportProvider;
        this.activeProvider = this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(desiredProviderId, StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals("SocketsHttpHandler", StringComparison.OrdinalIgnoreCase))
                          ?? this.availableProviders.FirstOrDefault();

        if (this.activeProvider == null)
        {
            throw new InvalidOperationException("No HTTP transport providers are registered in the system container.");
        }

        this.logger.Info("DynamicHttpTransportProxy initialized with active provider: {0} ({1})", this.activeProvider.DisplayName, this.activeProvider.ProviderId);
    }

    public IEnumerable<IHttpTransportProvider> GetProviders()
    {
        return this.availableProviders;
    }

    public IHttpTransportProvider GetProvider(string providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        return this.availableProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<HttpTransportHealthCheckResult> ProbeProviderAsync(string providerId)
    {
        var provider = this.GetProvider(providerId);
        if (provider == null)
        {
            return new HttpTransportHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"HTTP transport provider '{providerId}' is not recognized or registered.",
                Warnings = { "Provider identifier not found in active provider registry." },
            };
        }

        return await provider.ProbeHealthAsync();
    }

    public async Task<HttpTransportSwitchResult> SwitchProviderAsync(string targetProviderId)
    {
        if (string.IsNullOrWhiteSpace(targetProviderId))
        {
            return new HttpTransportSwitchResult
            {
                Success = false,
                Error = "Target provider ID must not be empty.",
            };
        }

        var targetProvider = this.GetProvider(targetProviderId);
        if (targetProvider == null)
        {
            return new HttpTransportSwitchResult
            {
                Success = false,
                Error = $"Target provider '{targetProviderId}' is not registered.",
            };
        }

        if (string.Equals(Volatile.Read(ref this.activeProvider).ProviderId, targetProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return new HttpTransportSwitchResult
            {
                Success = true,
                PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"HTTP transport provider '{targetProvider.DisplayName}' is already active.",
            };
        }

        HttpTransportProviderSwitchedEvent switchedEvent = null;
        HttpTransportSwitchResult result;

        await this.switchLock.WaitAsync();
        try
        {
            var health = await targetProvider.ProbeHealthAsync();
            if (!health.IsHealthy)
            {
                return new HttpTransportSwitchResult
                {
                    Success = false,
                    PreviousProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                    ActiveProvider = Volatile.Read(ref this.activeProvider).ProviderId,
                    Error = $"Cannot switch to provider '{targetProvider.DisplayName}': health check failed ({health.StatusMessage}).",
                };
            }

            var previousProvider = Volatile.Read(ref this.activeProvider);
            Volatile.Write(ref this.activeProvider, targetProvider);

            this.configService?.SaveConfigDictionary(new Dictionary<string, object>
            {
                { "ActiveHttpTransportProvider", targetProvider.ProviderId },
            });

            this.logger.Info("HTTP transport provider switched: {0} -> {1}", previousProvider.ProviderId, targetProvider.ProviderId);
            switchedEvent = new HttpTransportProviderSwitchedEvent(previousProvider.ProviderId, targetProvider.ProviderId);

            result = new HttpTransportSwitchResult
            {
                Success = true,
                PreviousProvider = previousProvider.ProviderId,
                ActiveProvider = targetProvider.ProviderId,
                Message = $"Successfully switched HTTP transport provider to {targetProvider.DisplayName}.",
            };
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Error switching HTTP transport provider to {0}", targetProviderId);
            return new HttpTransportSwitchResult
            {
                Success = false,
                PreviousProvider = Volatile.Read(ref this.activeProvider)?.ProviderId,
                ActiveProvider = Volatile.Read(ref this.activeProvider)?.ProviderId,
                Error = $"Hot-swap failed: {ex.Message}",
            };
        }
        finally
        {
            this.switchLock.Release();
        }

        if (switchedEvent != null)
        {
            this.eventAggregator?.PublishEvent(switchedEvent);
        }

        return result;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var currentProvider = Volatile.Read(ref this.activeProvider);
        if (currentProvider is FlareSolverrTransportProvider ||
            string.Equals(currentProvider.ProviderId, "FlareSolverr", StringComparison.OrdinalIgnoreCase))
        {
            return await currentProvider.SendAsync(request, cancellationToken);
        }

        var flareSolverr = this.GetProvider("FlareSolverr");
        if (flareSolverr == null)
        {
            return await currentProvider.SendAsync(request, cancellationToken);
        }

        HttpRequestMessage clonedRequest = null;
        try
        {
            clonedRequest = await CloneHttpRequestMessageAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Failed to clone HttpRequestMessage for potential FlareSolverr failover");
        }

        HttpResponseMessage response;
        try
        {
            response = await currentProvider.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            clonedRequest?.Dispose();
            throw;
        }

        try
        {
            if (await AntiBotChallengeDetector.IsChallengeAsync(response))
            {
                var health = await flareSolverr.ProbeHealthAsync();
                if (health.IsHealthy && clonedRequest != null)
                {
                    this.logger.Warn(
                        "Anti-bot / Cloudflare challenge detected (HTTP {0}) for {1}. Transparently failing over to FlareSolverr.",
                        (int)response.StatusCode,
                        request.RequestUri);

                    response.Dispose();
                    var failoverRequest = clonedRequest;
                    clonedRequest = null; // Ownership transferred to FlareSolverr
                    return await flareSolverr.SendAsync(failoverRequest, cancellationToken);
                }
                else
                {
                    this.logger.Debug(
                        "Anti-bot challenge detected for {0}, but FlareSolverr is not healthy ({1}). Returning original response.",
                        request.RequestUri,
                        health.StatusMessage);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error during AntiBot challenge detection or FlareSolverr failover for {0}", request.RequestUri);
        }
        finally
        {
            clonedRequest?.Dispose();
        }

        return response;
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage req, CancellationToken cancellationToken = default)
    {
        var clone = new HttpRequestMessage(req.Method, req.RequestUri)
        {
            Version = req.Version,
            VersionPolicy = req.VersionPolicy,
        };

        foreach (var header in req.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (req.Content != null)
        {
            var contentBytes = await req.Content.ReadAsByteArrayAsync(cancellationToken);
            var cloneContent = new ByteArrayContent(contentBytes);
            foreach (var header in req.Content.Headers)
            {
                cloneContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Content = cloneContent;
        }

        foreach (var option in req.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }

    public void Handle(ConfigSavedEvent message)
    {
        var desiredProviderId = this.configService?.ActiveHttpTransportProvider;
        if (!string.IsNullOrWhiteSpace(desiredProviderId) &&
            !string.Equals(this.ActiveProviderId, desiredProviderId, StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                try
                {
                    await this.SwitchProviderAsync(desiredProviderId);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Failed to switch active HTTP transport on ConfigSavedEvent to {0}", desiredProviderId);
                }
            });
        }
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.switchLock.Dispose();
        }
    }
}
