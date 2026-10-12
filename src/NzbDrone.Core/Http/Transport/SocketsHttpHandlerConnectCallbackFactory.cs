// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
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
            IPAddress[] resolvedAddresses = null;

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
                catch (Exception ex)
                {
                    logger?.Trace(ex, "DNS resolution failed for {0}, falling back to DnsEndPoint connect", host);
                }
            }

            if (resolvedAddresses != null && resolvedAddresses.Length > 0)
            {
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
            }

            var fallbackSocket = context.DnsEndPoint.AddressFamily == AddressFamily.Unspecified
                ? new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true }
                : new Socket(context.DnsEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

            if (!string.IsNullOrWhiteSpace(iface) && networkBindingService != null)
            {
                networkBindingService.BindSocket(fallbackSocket, iface);
            }

            try
            {
                await fallbackSocket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                return new NetworkStream(fallbackSocket, ownsSocket: true);
            }
            catch
            {
                fallbackSocket.Dispose();
                throw;
            }
        };
    }
}
