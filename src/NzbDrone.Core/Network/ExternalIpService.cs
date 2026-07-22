using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network;

public interface IExternalIpService
{
    string CachedIp { get; }
    Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default);
}

public class ExternalIpService : BackgroundService, IExternalIpService
{
    private const string PrimaryEndpointTemplate = "https://www.leecharr.net/ip/?uuid={0}";
    private const string PrimaryHttpEndpointTemplate = "http://www.leecharr.net/ip/?uuid={0}";

    private static readonly TimeSpan FallbackInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private static readonly string[] FallbackSources =
    {
        "https://api.ipify.org",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
        "https://checkip.amazonaws.com"
    };

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10)
    })
    { Timeout = TimeSpan.FromSeconds(5) };

    private readonly HttpClient _client;
    private readonly IConfigService _configService;
    private readonly Logger _logger;
    private readonly SemaphoreSlim _fetchLock = new(1, 1);
    private string _cachedIp = "";
    private DateTime _lastFetch = DateTime.MinValue;
    private volatile bool _networkChanged;

    public string CachedIp => _cachedIp;

    public ExternalIpService(IConfigService configService, HttpClient httpClient = null)
    {
        _configService = configService;
        _client = httpClient ?? SharedClient;
        _logger = LogManager.GetCurrentClassLogger();
    }

    public ExternalIpService(HttpClient httpClient)
        : this(null, httpClient)
    {
    }

    public ExternalIpService()
        : this(null, null)
    {
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await FetchExternalIpAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

                if (_networkChanged)
                {
                    _networkChanged = false;
                    _logger.Info("Network change detected, refreshing external IP");
                    await RefreshIp(stoppingToken);
                }
                else if (DateTime.UtcNow - _lastFetch > FallbackInterval)
                {
                    await RefreshIp(stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        }
    }

    private void OnNetworkChanged(object sender, EventArgs e)
    {
        _networkChanged = true;
    }

    private async Task RefreshIp(CancellationToken cancellationToken)
    {
        try
        {
            var oldIp = _cachedIp;
            var newIp = await FetchExternalIpAsync(cancellationToken);

            if (!string.IsNullOrEmpty(newIp) && newIp != oldIp)
            {
                _logger.Info("External IP changed: {0} -> {1}", oldIp, newIp);
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "External IP refresh failed");
        }
    }

    public async Task<string> GetExternalIpAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedIp) && DateTime.UtcNow - _lastFetch < CacheDuration)
        {
            return _cachedIp;
        }

        return await FetchExternalIpAsync(cancellationToken);
    }

    private async Task<string> FetchExternalIpAsync(CancellationToken cancellationToken)
    {
        if (!await _fetchLock.WaitAsync(0, cancellationToken))
        {
            return _cachedIp;
        }

        try
        {
            var uuid = _configService?.InstanceUuid;
            if (string.IsNullOrWhiteSpace(uuid))
            {
                uuid = Guid.NewGuid().ToString().ToLowerInvariant();
            }

            var sources = new List<string>
            {
                string.Format(PrimaryEndpointTemplate, Uri.EscapeDataString(uuid)),
                string.Format(PrimaryHttpEndpointTemplate, Uri.EscapeDataString(uuid))
            };
            sources.AddRange(FallbackSources);

            foreach (var source in sources)
            {
                try
                {
                    var response = await _client.GetStringAsync(source, cancellationToken);

                    if (TryExtractIpFromResponse(response, out var ip))
                    {
                        _cachedIp = ip;
                        _lastFetch = DateTime.UtcNow;
                        _logger.Debug("External IP from {0}: {1}", source, ip);
                        return ip;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to get external IP from {0}", source);
                }
            }

            return _cachedIp;
        }
        finally
        {
            _fetchLock.Release();
        }
    }

    public static bool TryExtractIpFromResponse(string responseText, out string ip)
    {
        ip = string.Empty;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        var trimmed = responseText.Trim();

        // 1. Try parsing JSON format (e.g. from leecharr.net/ip/?uuid=...)
        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("ip", out var ipProp) && ipProp.ValueKind == JsonValueKind.String)
                {
                    var extractedIp = ipProp.GetString();
                    if (IPAddress.TryParse(extractedIp, out _))
                    {
                        ip = extractedIp;
                        return true;
                    }
                }

                if (root.TryGetProperty("ip_address", out var ipAddrProp) && ipAddrProp.ValueKind == JsonValueKind.String)
                {
                    var extractedIp = ipAddrProp.GetString();
                    if (IPAddress.TryParse(extractedIp, out _))
                    {
                        ip = extractedIp;
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON, proceed to plain text
        }

        // 2. Try parsing plain-text IP
        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            ip = parsed.ToString();
            return true;
        }

        return false;
    }
}
