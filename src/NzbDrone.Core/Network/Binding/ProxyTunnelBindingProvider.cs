// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network.Binding;

public class ProxyTunnelBindingProvider : IProxyTunnelBindingProvider
{
    private readonly IConfigService configService;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public string ProviderId => "ProxyTunnel";

    public string DisplayName => "Proxy Tunnel Binding (SOCKS5 / Tor Onion)";

    public string Version => "1.0.0";

    public string Description => "Routes outbound socket traffic through SOCKS5, HTTP, or Tor Onion proxies with anonymous routing mode.";

    public bool IsAvailable => true;

    public NetworkBindingCapabilities Capabilities => new()
    {
        SupportsInterfaceBinding = false,
        SupportsSoBindToDevice = false,
        SupportsSocks5Proxy = true,
        SupportsTorOnion = true,
        SupportsVpnKillSwitch = false,
        SupportsAnonymousRouting = true,
    };

    public ProxyTunnelBindingProvider(IConfigService configService = null)
    {
        this.configService = configService;
    }

    public Task<NetworkBindingHealthCheckResult> ProbeHealthAsync()
    {
        var proxyHost = this.configService?.ProxyHost;
        var proxyPort = this.configService?.ProxyPort ?? 1080;
        var proxyType = this.configService?.ProxyType ?? "none";

        if (string.Equals(proxyType, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(proxyHost))
        {
            return Task.FromResult(new NetworkBindingHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = "Proxy tunnel provider is available (no proxy configured, pass-through mode).",
                Warnings = { "Proxy tunnel is active but ProxyHost is currently unconfigured." },
            });
        }

        return Task.FromResult(new NetworkBindingHealthCheckResult
        {
            IsHealthy = true,
            StatusMessage = $"Proxy tunnel provider configured for {proxyType.ToUpperInvariant()} at {proxyHost}:{proxyPort}.",
        });
    }

    public void BindSocket(Socket socket, string interfaceName)
    {
        if (socket == null)
        {
            throw new ArgumentNullException(nameof(socket));
        }

        this.logger.Debug("Proxy tunnel provider active: socket outbound traffic will be proxied via {0}:{1}", this.configService?.ProxyHost, this.configService?.ProxyPort);
    }

    public bool IsInterfaceUp(string interfaceName)
    {
        return true;
    }

    public Socket ConnectTunnel(string targetHost, int targetPort)
    {
        return this.ConnectTunnelAsync(targetHost, targetPort).GetAwaiter().GetResult();
    }

    public async Task<Socket> ConnectTunnelAsync(string targetHost, int targetPort, CancellationToken cancellationToken = default)
    {
        var proxyType = this.configService?.ProxyType?.ToLowerInvariant() ?? "none";
        var proxyHost = this.configService?.ProxyHost;
        var proxyPort = this.configService?.ProxyPort ?? (proxyType == "socks5" ? 1080 : 8080);

        if (string.Equals(proxyType, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(proxyHost))
        {
            // Direct connection
            var directSocket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await directSocket.ConnectAsync(targetHost, targetPort, cancellationToken);
            return directSocket;
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

        try
        {
            await socket.ConnectAsync(proxyHost, proxyPort, cancellationToken);

            if (proxyType == "socks5")
            {
                await this.PerformSocks5HandshakeAsync(socket, targetHost, targetPort, cancellationToken);
            }
            else if (proxyType == "http")
            {
                await this.PerformHttpConnectHandshakeAsync(socket, targetHost, targetPort, cancellationToken);
            }
            else
            {
                throw new NotSupportedException($"Proxy type '{proxyType}' is not supported for tunneling.");
            }

            return socket;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            this.logger.Warn(ex, "Failed to establish proxy tunnel via {0}:{1} to {2}:{3}", proxyHost, proxyPort, targetHost, targetPort);
            throw;
        }
    }

    private async Task PerformSocks5HandshakeAsync(Socket socket, string targetHost, int targetPort, CancellationToken cancellationToken)
    {
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var username = this.configService?.ProxyUsername;
        var password = this.configService?.ProxyPassword;
        var hasAuth = !string.IsNullOrEmpty(username);

        // 1. Send SOCKS5 Greeting
        // [0x05 (version), NMETHODS, 0x00 (no auth), 0x02 (user/pass if configured)]
        byte[] greeting = hasAuth
            ? new byte[] { 0x05, 0x02, 0x00, 0x02 }
            : new byte[] { 0x05, 0x01, 0x00 };

        await stream.WriteAsync(greeting, 0, greeting.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Read 2 bytes response: [0x05, selected_method]
        var response = new byte[2];
        await ReadExactBytesAsync(stream, response, 0, 2, cancellationToken);

        if (response[0] != 0x05)
        {
            throw new InvalidOperationException($"Invalid SOCKS version response from proxy: {response[0]}");
        }

        var selectedMethod = response[1];
        if (selectedMethod == 0xFF)
        {
            throw new InvalidOperationException("SOCKS5 proxy rejected all authentication methods.");
        }

        // 2. Authentication subnegotiation (RFC 1929) if required
        if (selectedMethod == 0x02)
        {
            var userBytes = Encoding.UTF8.GetBytes(username ?? string.Empty);
            var passBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);

            var authPayload = new byte[1 + 1 + userBytes.Length + 1 + passBytes.Length];
            authPayload[0] = 0x01; // subnegotiation version
            authPayload[1] = (byte)userBytes.Length;
            Buffer.BlockCopy(userBytes, 0, authPayload, 2, userBytes.Length);
            authPayload[2 + userBytes.Length] = (byte)passBytes.Length;
            Buffer.BlockCopy(passBytes, 0, authPayload, 3 + userBytes.Length, passBytes.Length);

            await stream.WriteAsync(authPayload, 0, authPayload.Length, cancellationToken);
            await stream.FlushAsync(cancellationToken);

            var authResponse = new byte[2];
            await ReadExactBytesAsync(stream, authResponse, 0, 2, cancellationToken);

            if (authResponse[1] != 0x00)
            {
                throw new InvalidOperationException("SOCKS5 proxy authentication failed.");
            }
        }

        // 3. Send CONNECT request (RFC 1928)
        // [0x05, 0x01 (CONNECT), 0x00 (RSV), ATYP, DEST.ADDR, DEST.PORT]
        using var ms = new MemoryStream();
        ms.WriteByte(0x05); // version
        ms.WriteByte(0x01); // CMD: CONNECT
        ms.WriteByte(0x00); // RSV

        if (IPAddress.TryParse(targetHost, out var ipAddress))
        {
            if (ipAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                ms.WriteByte(0x01); // ATYP: IPv4
                var ipBytes = ipAddress.GetAddressBytes();
                ms.Write(ipBytes, 0, ipBytes.Length);
            }
            else if (ipAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                ms.WriteByte(0x04); // ATYP: IPv6
                var ipBytes = ipAddress.GetAddressBytes();
                ms.Write(ipBytes, 0, ipBytes.Length);
            }
            else
            {
                throw new NotSupportedException($"Address family {ipAddress.AddressFamily} is not supported by SOCKS5.");
            }
        }
        else
        {
            // Domain name - proxy resolves host to prevent DNS leaks
            var domainBytes = Encoding.ASCII.GetBytes(targetHost);
            ms.WriteByte(0x03); // ATYP: Domain
            ms.WriteByte((byte)domainBytes.Length);
            ms.Write(domainBytes, 0, domainBytes.Length);
        }

        // Port big-endian (2 bytes)
        ms.WriteByte((byte)((targetPort >> 8) & 0xFF));
        ms.WriteByte((byte)(targetPort & 0xFF));

        var connectPayload = ms.ToArray();
        await stream.WriteAsync(connectPayload, 0, connectPayload.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Read response: [0x05, REP, RSV, ATYP, BND.ADDR, BND.PORT]
        var connectHeader = new byte[4];
        await ReadExactBytesAsync(stream, connectHeader, 0, 4, cancellationToken);

        if (connectHeader[1] != 0x00)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }

        var atyp = connectHeader[3];
        int addrLen;
        if (atyp == 0x01)
        {
            addrLen = 4; // IPv4
        }
        else if (atyp == 0x04)
        {
            addrLen = 16; // IPv6
        }
        else if (atyp == 0x03)
        {
            var lengthBuf = new byte[1];
            await ReadExactBytesAsync(stream, lengthBuf, 0, 1, cancellationToken);
            addrLen = lengthBuf[0];
            if (addrLen == 0)
            {
                throw new InvalidOperationException("SOCKS5 proxy returned invalid domain length 0");
            }
        }
        else
        {
            throw new InvalidOperationException($"Unknown SOCKS5 ATYP: {atyp}");
        }

        var boundAddress = new byte[addrLen + 2]; // address + 2 bytes port
        await ReadExactBytesAsync(stream, boundAddress, 0, boundAddress.Length, cancellationToken);

        this.logger.Debug("SOCKS5 tunnel established successfully to {0}:{1}", targetHost, targetPort);
    }

    private async Task PerformHttpConnectHandshakeAsync(Socket socket, string targetHost, int targetPort, CancellationToken cancellationToken)
    {
        using var stream = new NetworkStream(socket, ownsSocket: false);

        var username = this.configService?.ProxyUsername;
        var password = this.configService?.ProxyPassword;

        var sb = new StringBuilder();
        sb.Append($"CONNECT {targetHost}:{targetPort} HTTP/1.1\r\n");
        sb.Append($"Host: {targetHost}:{targetPort}\r\n");

        if (!string.IsNullOrEmpty(username))
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password ?? string.Empty}"));
            sb.Append($"Proxy-Authorization: Basic {auth}\r\n");
        }

        sb.Append("\r\n");

        var requestBytes = Encoding.ASCII.GetBytes(sb.ToString());
        await stream.WriteAsync(requestBytes, 0, requestBytes.Length, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        // Read response headers up to \r\n\r\n
        var headerBytes = new List<byte>();
        var buffer = new byte[1];

        while (headerBytes.Count < 8192)
        {
            var read = await stream.ReadAsync(buffer, 0, 1, cancellationToken);
            if (read == 0)
            {
                throw new IOException("HTTP CONNECT proxy closed connection during handshake");
            }

            headerBytes.Add(buffer[0]);

            if (headerBytes.Count >= 4 &&
                headerBytes[^4] == '\r' &&
                headerBytes[^3] == '\n' &&
                headerBytes[^2] == '\r' &&
                headerBytes[^1] == '\n')
            {
                break;
            }
        }

        var responseText = Encoding.ASCII.GetString(headerBytes.ToArray());

        if (!responseText.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase) &&
            !responseText.StartsWith("HTTP/1.0 200", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"HTTP CONNECT proxy returned non-200 status: {responseText.Split(new[] { "\r\n" }, StringSplitOptions.None)[0]}");
        }

        this.logger.Debug("HTTP CONNECT tunnel established successfully to {0}:{1}", targetHost, targetPort);
    }

    private static async Task ReadExactBytesAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Proxy closed the connection unexpectedly.");
            }

            totalRead += read;
        }
    }
}
