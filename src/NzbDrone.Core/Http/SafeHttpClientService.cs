// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Http;

public class SafeHttpClientService : ISafeHttpClientService, IDisposable
{
    public const long DefaultMaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly Logger logger;

    public SafeHttpClientService(HttpClient httpClient = null)
    {
        this.logger = LogManager.GetCurrentClassLogger();

        if (httpClient != null)
        {
            this.httpClient = httpClient;
            this.ownsClient = false;
        }
        else
        {
            var handler = this.CreateSafeSocketsHttpHandler();
            this.httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(30),
            };
            this.ownsClient = true;
        }
    }

    public SafeHttpClientService(HttpMessageHandler handler, bool disposeHandler = true)
    {
        this.logger = LogManager.GetCurrentClassLogger();
        this.httpClient = new HttpClient(handler, disposeHandler)
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        this.ownsClient = true;
    }

    public async Task<byte[]> DownloadBytesAsync(string url, long maxSizeBytes = DefaultMaxSizeBytes, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be empty.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL format: '{url}'", nameof(url));
        }

        return await this.DownloadBytesAsync(uri, maxSizeBytes, cancellationToken);
    }

    public async Task<byte[]> DownloadBytesAsync(Uri uri, long maxSizeBytes = DefaultMaxSizeBytes, CancellationToken cancellationToken = default)
    {
        this.ValidateUri(uri);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await this.httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength.HasValue)
        {
            var contentLength = response.Content.Headers.ContentLength.Value;
            if (contentLength > maxSizeBytes)
            {
                throw new InvalidOperationException($"Response Content-Length ({contentLength} bytes) exceeds maximum allowed size of {maxSizeBytes} bytes.");
            }
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytesRead = 0;

        int bytesRead;
        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            totalBytesRead += bytesRead;
            if (totalBytesRead > maxSizeBytes)
            {
                throw new InvalidOperationException($"Response body size exceeded maximum allowed limit of {maxSizeBytes} bytes.");
            }

            memoryStream.Write(buffer, 0, bytesRead);
        }

        return memoryStream.ToArray();
    }

    public void ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be empty.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"Invalid URL format: '{url}'", nameof(url));
        }

        this.ValidateUri(uri);
    }

    public void ValidateUri(Uri uri)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException($"Unsupported URI scheme '{uri.Scheme}'. Only HTTP and HTTPS schemes are allowed.");
        }

        var host = uri.DnsSafeHost;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityException("SSRF blocked: 'localhost' is prohibited.");
        }

        if (IPAddress.TryParse(host, out var directIp))
        {
            if (this.IsBlockedIp(directIp))
            {
                throw new SecurityException($"SSRF blocked: IP address '{directIp}' is prohibited.");
            }
        }
        else
        {
            try
            {
                var addresses = Dns.GetHostAddresses(host);
                if (addresses != null && addresses.Length > 0)
                {
                    foreach (var addr in addresses)
                    {
                        if (this.IsBlockedIp(addr))
                        {
                            throw new SecurityException($"SSRF blocked: Host '{host}' resolves to prohibited IP address '{addr}'.");
                        }
                    }
                }
            }
            catch (SocketException ex)
            {
                this.logger.Debug(ex, "DNS resolution failed for host '{0}' during validation; connection handler will enforce.", host);
            }
        }
    }

    public bool IsBlockedIp(IPAddress ip)
    {
        if (ip == null)
        {
            return true;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.None) || ip.Equals(IPAddress.IPv6None))
        {
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();

            // 0.0.0.0/8 (Current network)
            if (bytes[0] == 0)
            {
                return true;
            }

            // 127.0.0.0/8 (Loopback)
            if (bytes[0] == 127)
            {
                return true;
            }

            // 169.254.0.0/16 (Link-local / Cloud metadata: 169.254.169.254)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return true;
            }

            // Broadcast
            if (bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 255 && bytes[3] == 255)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || IPAddress.IsLoopback(ip))
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            // fe80::/10 (Link-local)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
            {
                return true;
            }

            // fec0::/10 (Site-local)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0xc0)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (this.ownsClient)
        {
            this.httpClient?.Dispose();
        }
    }

    private SocketsHttpHandler CreateSafeSocketsHttpHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;

                IPAddress[] addresses;
                if (IPAddress.TryParse(host, out var parsedIp))
                {
                    addresses = new[] { parsedIp };
                }
                else
                {
                    addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                }

                if (addresses == null || addresses.Length == 0)
                {
                    throw new SecurityException($"Unable to resolve IP address for host '{host}'.");
                }

                foreach (var addr in addresses)
                {
                    if (this.IsBlockedIp(addr))
                    {
                        throw new SecurityException($"SSRF blocked: IP address '{addr}' for host '{host}' is prohibited.");
                    }
                }

                var targetIp = addresses[0];
                var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };

                try
                {
                    await socket.ConnectAsync(new IPEndPoint(targetIp, port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };
    }
}
