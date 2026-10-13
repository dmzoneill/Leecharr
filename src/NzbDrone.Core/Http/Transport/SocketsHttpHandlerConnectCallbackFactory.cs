// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Vpn;

namespace NzbDrone.Core.Http.Transport;

internal static class SocketsHttpHandlerConnectCallbackFactory
{
    public static Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>> Create(
        IConfigService configService,
        INetworkBindingService networkBindingService,
        IVpnKillSwitchService vpnKillSwitchService,
        Logger logger = null)
    {
        return async (context, cancellationToken) =>
        {
            if (vpnKillSwitchService?.IsFailClosedActive == true)
            {
                throw new SocketException((int)SocketError.NetworkUnreachable);
            }

            var iface = configService?.BindInterface;
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;
            var allowPrivateNetworkRequests = configService?.AllowPrivateNetworkRequests ?? false;
            var allowedSsrfSubnets = configService?.AllowedSsrfSubnets;
            IPAddress[] resolvedAddresses;

            if (IPAddress.TryParse(host, out var parsedIp))
            {
                resolvedAddresses = [parsedIp];
            }
            else
            {
                try
                {
                    resolvedAddresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.Trace(ex, "DNS resolution failed for {0}; refusing connection without an address check", host);
                    throw new SecurityException($"Unable to resolve IP address for host '{host}'.", ex);
                }
            }

            SsrfAddressPolicy.EnsureConnectable(host, resolvedAddresses, allowPrivateNetworkRequests, allowedSsrfSubnets);

            if (networkBindingService?.ActiveProvider is IProxyTunnelBindingProvider proxyProvider)
            {
                var tunneled = await proxyProvider.ConnectTunnelAsync(host, port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(tunneled, ownsSocket: true);
            }

            Exception lastError = null;
            foreach (var address in resolvedAddresses)
            {
                var targetIp = address;
                if (targetIp.IsIPv4MappedToIPv6)
                {
                    targetIp = targetIp.MapToIPv4();
                }

                var socket = new Socket(targetIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                if (!string.IsNullOrWhiteSpace(iface) && networkBindingService != null)
                {
                    networkBindingService.BindSocket(socket, iface);
                }

                try
                {
                    await socket.ConnectAsync(new IPEndPoint(targetIp, port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex) when (ex is SocketException or IOException)
                {
                    lastError = ex;
                    socket.Dispose();
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            if (lastError != null)
            {
                throw lastError;
            }

            throw new SocketException((int)SocketError.HostUnreachable);
        };
    }
}
