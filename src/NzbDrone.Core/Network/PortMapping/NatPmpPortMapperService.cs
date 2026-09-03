// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Network.PortMapping;

public class NatPmpPortMapperService : INatPmpPortMapperService
{
    private const int NatPmpPort = 5351;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public static IPAddress DiscoverDefaultGateway()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var props = ni.GetIPProperties();
                    var gw = props.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (gw != null && !IPAddress.IsLoopback(gw.Address) && !Equals(gw.Address, IPAddress.Any))
                    {
                        return gw.Address;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public async Task<IPAddress> GetExternalIpAddressAsync(IPAddress gateway = null, CancellationToken cancellationToken = default)
    {
        var targetGateway = gateway ?? DiscoverDefaultGateway();
        if (targetGateway == null)
        {
            return null;
        }

        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 1000;
            udp.Client.SendTimeout = 1000;

            // RFC 6886 external address request: 2 bytes [0x00, 0x00]
            var request = new byte[] { 0x00, 0x00 };
            var endpoint = new IPEndPoint(targetGateway, NatPmpPort);

            await udp.SendAsync(request, request.Length, endpoint);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(1500);

            var receiveTask = udp.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(-1, cts.Token));

            if (completedTask == receiveTask)
            {
                var result = await receiveTask;
                var buffer = result.Buffer;

                // Expected response: 12 bytes [0x00, 0x80, result(2), epoch(4), ip(4)]
                if (buffer.Length >= 12 && buffer[0] == 0x00 && buffer[1] == 0x80)
                {
                    var resultCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
                    if (resultCode == 0)
                    {
                        var ipBytes = buffer.AsSpan(8, 4).ToArray();
                        return new IPAddress(ipBytes);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "NAT-PMP external IP resolution failed against {0}", targetGateway);
        }

        return null;
    }

    public async Task<NatPmpMappingResult> MapPortAsync(
        int internalPort,
        NatPmpProtocol protocol,
        int suggestedExternalPort = 0,
        int lifetimeSeconds = 3600,
        IPAddress gateway = null,
        CancellationToken cancellationToken = default)
    {
        var targetGateway = gateway ?? DiscoverDefaultGateway();
        if (targetGateway == null)
        {
            return new NatPmpMappingResult
            {
                Success = false,
                InternalPort = internalPort,
                ErrorMessage = "No IPv4 default gateway found for NAT-PMP.",
            };
        }

        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 1000;
            udp.Client.SendTimeout = 1000;

            // RFC 6886 mapping request: 12 bytes
            // [0x00, opcode(1=udp, 2=tcp), reserved(2), internalPort(2), suggestedExternalPort(2), lifetime(4)]
            var request = new byte[12];
            request[0] = 0x00;
            request[1] = (byte)protocol;
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)internalPort);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), (ushort)(suggestedExternalPort > 0 ? suggestedExternalPort : internalPort));
            BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8, 4), (uint)lifetimeSeconds);

            var endpoint = new IPEndPoint(targetGateway, NatPmpPort);
            await udp.SendAsync(request, request.Length, endpoint);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(1500);

            var receiveTask = udp.ReceiveAsync();
            var completedTask = await Task.WhenAny(receiveTask, Task.Delay(-1, cts.Token));

            if (completedTask == receiveTask)
            {
                var result = await receiveTask;
                var buffer = result.Buffer;

                // Expected response: 16 bytes [0x00, 0x80+opcode, result(2), epoch(4), internal(2), external(2), lifetime(4)]
                var expectedOpcode = (byte)(0x80 + (byte)protocol);
                if (buffer.Length >= 16 && buffer[0] == 0x00 && buffer[1] == expectedOpcode)
                {
                    var resultCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
                    if (resultCode == 0)
                    {
                        var mappedInternal = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(8, 2));
                        var mappedExternal = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(10, 2));
                        var grantedLifetime = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12, 4));

                        this.logger.Info(
                            "NAT-PMP port mapping successful via {0}: {1} {2} -> {3} (Lifetime: {4}s)",
                            targetGateway,
                            protocol,
                            mappedInternal,
                            mappedExternal,
                            grantedLifetime);

                        return new NatPmpMappingResult
                        {
                            Success = true,
                            InternalPort = mappedInternal,
                            ExternalPort = mappedExternal,
                            LifetimeSeconds = (int)grantedLifetime,
                            GatewayAddress = targetGateway,
                        };
                    }

                    return new NatPmpMappingResult
                    {
                        Success = false,
                        InternalPort = internalPort,
                        ErrorMessage = $"NAT-PMP gateway returned error code: {resultCode}",
                    };
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "NAT-PMP port mapping request failed against {0}", targetGateway);
        }

        return new NatPmpMappingResult
        {
            Success = false,
            InternalPort = internalPort,
            ErrorMessage = $"NAT-PMP did not receive a response from gateway {targetGateway}.",
        };
    }

    public async Task<bool> UnmapPortAsync(
        int internalPort,
        NatPmpProtocol protocol,
        IPAddress gateway = null,
        CancellationToken cancellationToken = default)
    {
        var result = await this.MapPortAsync(internalPort, protocol, 0, lifetimeSeconds: 0, gateway, cancellationToken);
        return result.Success;
    }
}
