// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Http.Transport;

public class CurlImpersonateTransportProvider : IHttpTransportProvider, IHttpOutboundProxyRefreshable, IDisposable
{
    private static readonly string[] CurlBinaryNames =
    {
        "curl-impersonate-chrome",
        "curl_chrome116",
        "curl_chrome110",
        "curl-impersonate",
    };

    private readonly SocketsHttpHandler handler;
    private readonly IConfigService configService;
    private readonly INetworkBindingService networkBindingService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private readonly string curlBinaryPathOverride;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();
    private bool disposed;

    internal SocketsHttpHandler Handler => this.handler;

    internal WebProxy Proxy => this.handler.Proxy as WebProxy;

    public string ProviderId => "CurlImpersonate";

    public string DisplayName => "curl-impersonate (Chrome / Firefox TLS JA3/JA4 Fingerprint)";

    public string Version => "0.6.1";

    public string Description => "Emulates Chrome/Firefox TLS handshakes, JA3/JA4 fingerprints, and HTTP/2 settings to bypass anti-bot protections.";

    public bool IsAvailable => TryResolveCurlBinaryPath(out _);

    public HttpTransportCapabilities Capabilities
    {
        get
        {
            var hasBinary = TryResolveCurlBinaryPath(out _);
            return new HttpTransportCapabilities
            {
                SupportsHttp3Quic = hasBinary,
                SupportsBrowserFingerprintEmulation = hasBinary,
                SupportsFlareSolverr = false,
                SupportsCustomProxy = true,
                SupportsTlsJa3Ja4Fingerprinting = hasBinary,
                SupportsCookieExtraction = true,
            };
        }
    }

    public CurlImpersonateTransportProvider(
        IConfigService configService = null,
        INetworkBindingService networkBindingService = null,
        IVpnKillSwitchService vpnKillSwitchService = null)
        : this(configService, networkBindingService, vpnKillSwitchService, null)
    {
    }

    internal CurlImpersonateTransportProvider(
        IConfigService configService,
        INetworkBindingService networkBindingService,
        IVpnKillSwitchService vpnKillSwitchService,
        string curlBinaryPathOverride)
    {
        this.configService = configService;
        this.networkBindingService = networkBindingService;
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.curlBinaryPathOverride = curlBinaryPathOverride;
        this.handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            ConnectCallback = SocketsHttpHandlerConnectCallbackFactory.Create(
                this.configService,
                this.networkBindingService,
                this.vpnKillSwitchService),
        };

        this.RefreshOutboundProxy();
    }

    public void RefreshOutboundProxy()
    {
        HttpOutboundProxyConfigurator.Apply(this.handler, this.configService, HttpOutboundProxyProfile.CurlImpersonate);
    }

    public Task<HttpTransportHealthCheckResult> ProbeHealthAsync()
    {
        if (this.vpnKillSwitchService?.IsFailClosedActive == true)
        {
            return Task.FromResult(new HttpTransportHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = "VPN kill switch is active. Outbound HTTP transport is fail-closed until the VPN interface is restored.",
            });
        }

        if (TryResolveCurlBinaryPath(out var binaryPath))
        {
            return Task.FromResult(new HttpTransportHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"curl-impersonate binary found ({Path.GetFileName(binaryPath)}). TLS JA3/JA4 browser emulation ready.",
            });
        }

        return Task.FromResult(new HttpTransportHealthCheckResult
        {
            IsHealthy = false,
            StatusMessage = "curl-impersonate binary (e.g. curl_chrome116) not detected in system PATH. TLS JA3/JA4 emulation unavailable.",
            Warnings = { "Install curl-impersonate and ensure a curl_chrome116 (or compatible) binary is on PATH." },
        });
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (this.vpnKillSwitchService?.IsFailClosedActive == true)
        {
            throw new HttpRequestException("VPN kill switch is active. Outbound HTTP transport is fail-closed until the VPN interface is restored.");
        }

        if (!TryResolveCurlBinaryPath(out var binaryPath))
        {
            throw new InvalidOperationException(
                "curl-impersonate binary is not available. Install curl-impersonate (e.g. curl_chrome116) or switch the HTTP transport provider.");
        }

        ApplyDefaultBrowserHeaders(request);

        return await SendViaCurlAsync(request, binaryPath, cancellationToken);
    }

    private static void ApplyDefaultBrowserHeaders(HttpRequestMessage request)
    {
        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        }

        if (!request.Headers.Contains("Sec-Ch-Ua"))
        {
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\"");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
        }
    }

    private async Task<HttpResponseMessage> SendViaCurlAsync(HttpRequestMessage request, string binaryPath, CancellationToken cancellationToken)
    {
        var headerFile = CreatePrivateTempFile();
        var bodyFile = CreatePrivateTempFile();
        string requestBodyFile = null;

        try
        {
            var startInfo = BuildCurlStartInfo(request, binaryPath, headerFile, bodyFile, ref requestBodyFile);
            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start curl-impersonate process.");
            }

            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    stderr.AppendLine(e.Data);
                }
            };
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    this.logger.Debug(ex, "Failed to terminate curl-impersonate after cancellation");
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                var details = stderr.ToString().Trim();
                throw new HttpRequestException(
                    string.IsNullOrWhiteSpace(details)
                        ? $"curl-impersonate exited with code {process.ExitCode}."
                        : $"curl-impersonate failed (exit {process.ExitCode}): {details}");
            }

            var headerText = await File.ReadAllTextAsync(headerFile, cancellationToken);
            var bodyBytes = await File.ReadAllBytesAsync(bodyFile, cancellationToken);
            return BuildHttpResponseMessage(request, headerText, bodyBytes);
        }
        finally
        {
            TryDeleteTempFile(headerFile);
            TryDeleteTempFile(bodyFile);
            TryDeleteTempFile(requestBodyFile);
        }
    }

    internal ProcessStartInfo BuildCurlStartInfo(
        HttpRequestMessage request,
        string binaryPath,
        string headerFile,
        string bodyFile,
        ref string requestBodyFile)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add("--show-error");
        startInfo.ArgumentList.Add("--location");
        startInfo.ArgumentList.Add("--compressed");
        startInfo.ArgumentList.Add("--max-time");
        startInfo.ArgumentList.Add("30");

        var bindInterface = this.configService?.BindInterface;
        if (!string.IsNullOrWhiteSpace(bindInterface))
        {
            startInfo.ArgumentList.Add("--interface");
            startInfo.ArgumentList.Add(bindInterface.Trim());
        }

        if (this.handler.UseProxy && this.handler.Proxy is WebProxy webProxy && webProxy.Address != null)
        {
            startInfo.ArgumentList.Add("--proxy");
            startInfo.ArgumentList.Add(webProxy.Address.ToString());

            if (webProxy.Credentials is NetworkCredential credential)
            {
                startInfo.ArgumentList.Add("--proxy-user");
                startInfo.ArgumentList.Add($"{credential.UserName}:{credential.Password}");
            }
        }

        if (!string.Equals(request.Method.Method, "GET", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-X");
            startInfo.ArgumentList.Add(request.Method.Method);
        }

        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                startInfo.ArgumentList.Add("-H");
                startInfo.ArgumentList.Add($"{header.Key}: {value}");
            }
        }

        if (request.Content != null)
        {
            foreach (var header in request.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    startInfo.ArgumentList.Add("-H");
                    startInfo.ArgumentList.Add($"{header.Key}: {value}");
                }
            }

            var contentBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            if (contentBytes.Length > 0)
            {
                requestBodyFile = CreatePrivateTempFile();
                File.WriteAllBytes(requestBodyFile, contentBytes);
                startInfo.ArgumentList.Add("--data-binary");
                startInfo.ArgumentList.Add("@" + requestBodyFile);
            }
        }

        startInfo.ArgumentList.Add("-D");
        startInfo.ArgumentList.Add(headerFile);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(bodyFile);
        startInfo.ArgumentList.Add(request.RequestUri?.ToString() ?? throw new InvalidOperationException("Request URI is required."));

        return startInfo;
    }

    internal static HttpResponseMessage BuildHttpResponseMessage(HttpRequestMessage request, string headerDump, byte[] bodyBytes)
    {
        if (!TryParseLastHttpResponse(headerDump, out var statusCode, out var headers))
        {
            throw new HttpRequestException("curl-impersonate returned a response that could not be parsed.");
        }

        var response = new HttpResponseMessage(statusCode)
        {
            RequestMessage = request,
            Content = new ByteArrayContent(bodyBytes ?? Array.Empty<byte>()),
        };

        foreach (var header in headers)
        {
            if (IsHopByHopHeader(header.Key))
            {
                continue;
            }

            if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (MediaTypeHeaderValue.TryParse(header.Value, out var mediaType))
                {
                    response.Content.Headers.ContentType = mediaType;
                }

                continue;
            }

            if (string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(header.Key, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }

    internal static bool TryParseLastHttpResponse(string headerDump, out HttpStatusCode statusCode, out Dictionary<string, string> headers)
    {
        statusCode = default;
        headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(headerDump))
        {
            return false;
        }

        var normalized = headerDump.Replace("\r\n", "\n", StringComparison.Ordinal);
        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        if (blocks.Length == 0)
        {
            return false;
        }

        var lastBlock = blocks[^1];
        var lines = lastBlock.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return false;
        }

        var statusLine = lines[0];
        if (!statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        statusCode = (HttpStatusCode)code;

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            headers[name] = value;
        }

        return true;
    }

    private bool TryResolveCurlBinaryPath(out string binaryPath)
    {
        if (!string.IsNullOrWhiteSpace(this.curlBinaryPathOverride) && File.Exists(this.curlBinaryPathOverride))
        {
            binaryPath = this.curlBinaryPathOverride;
            return true;
        }

        binaryPath = ResolveCurlBinaryFromPath();
        return binaryPath != null;
    }

    internal static string ResolveCurlBinaryFromPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var dir in paths)
        {
            foreach (var bin in CurlBinaryNames)
            {
                var full = Path.Combine(dir, bin);
                if (File.Exists(full))
                {
                    return full;
                }
            }
        }

        return null;
    }

    private static bool IsHopByHopHeader(string name)
    {
        return name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
            || name.Equals("TE", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Trailer", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePrivateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }

        return path;
    }

    private static void TryDeleteTempFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort temp cleanup.
        }
    }

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.handler.Dispose();
        }
    }
}
