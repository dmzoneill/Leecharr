// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Network.PortMapping;

public class NatPmpPortMapperService : INatPmpPortMapperService, IAsyncDisposable
{
    private const int NatPmpPort = 5351;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<(int InternalPort, NatPmpProtocol Protocol), ActivePortMapping> activeMappings = new();
    private readonly Timer renewalTimer;
    private readonly SemaphoreSlim renewalLock = new(1, 1);
    private readonly int gatewayPort;
    private readonly string boundInterface;
    private readonly IConfigService configService;

    private int isRunning = 1;
    private int isDisposed;
    private uint? lastObservedEpoch;
    private IPAddress lastKnownGateway;

    public NatPmpPortMapperService(
        int gatewayPort = NatPmpPort,
        string boundInterface = null,
        IConfigService configService = null)
    {
        this.gatewayPort = gatewayPort > 0 ? gatewayPort : NatPmpPort;
        this.boundInterface = boundInterface;
        this.configService = configService;

        // Periodic lease renewal check every 30 seconds
        this.renewalTimer = new Timer(
            _ => _ = this.CheckAndRenewMappingsAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
    }

    public IReadOnlyCollection<ActivePortMapping> ActiveMappings => this.activeMappings.Values.ToList();

    public static IPAddress DiscoverDefaultGateway(string boundInterface = null)
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            // 1. If a specific bound interface is provided and active, prioritize its gateway
            if (!string.IsNullOrWhiteSpace(boundInterface) &&
                !string.Equals(boundInterface, "any", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(boundInterface, "all", StringComparison.OrdinalIgnoreCase))
            {
                var boundNic = interfaces.FirstOrDefault(n =>
                    string.Equals(n.Name, boundInterface, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n.Id, boundInterface, StringComparison.OrdinalIgnoreCase));

                if (boundNic != null)
                {
                    var gw = GetGatewayFromInterface(boundNic);
                    if (gw != null)
                    {
                        return gw;
                    }
                }
            }

            // 2. Look for VPN / Tunnel interfaces first (tun, wg, ppp, tap, vpn, utun, etc.)
            var vpnNic = interfaces.FirstOrDefault(n =>
                n.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) ||
                n.Name.StartsWith("wg", StringComparison.OrdinalIgnoreCase) ||
                n.Name.StartsWith("ppp", StringComparison.OrdinalIgnoreCase) ||
                n.Name.StartsWith("tap", StringComparison.OrdinalIgnoreCase) ||
                n.Name.StartsWith("vpn", StringComparison.OrdinalIgnoreCase) ||
                n.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase));

            if (vpnNic != null)
            {
                var vpnGw = GetGatewayFromInterface(vpnNic);
                if (vpnGw != null)
                {
                    return vpnGw;
                }
            }

            // 3. Fall back to non-Docker interfaces or any interface with a gateway
            // Filter out default Docker bridge IP 172.17.0.1 if other non-docker gateways are available
            IPAddress fallbackGateway = null;
            foreach (var ni in interfaces)
            {
                var gw = GetGatewayFromInterface(ni);
                if (gw != null)
                {
                    var gwStr = gw.ToString();
                    if (gwStr == "172.17.0.1")
                    {
                        fallbackGateway ??= gw;
                    }
                    else
                    {
                        return gw;
                    }
                }
            }

            return fallbackGateway;
        }
        catch
        {
            return null;
        }
    }

    private static IPAddress GetGatewayFromInterface(NetworkInterface ni)
    {
        try
        {
            var props = ni.GetIPProperties();
            var gw = props.GatewayAddresses
                .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork &&
                                     !IPAddress.IsLoopback(g.Address) &&
                                     !Equals(g.Address, IPAddress.Any) &&
                                     !Equals(g.Address, IPAddress.None));

            if (gw != null)
            {
                return gw.Address;
            }

            // For VPN / point-to-point interfaces where GatewayAddresses is empty in Linux,
            // check if there is a unicast IPv4 address and resolve the default subnet gateway (e.g. .1)
            var isVpnOrPointToPoint = ni.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                                      ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                                      ni.Name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) ||
                                      ni.Name.StartsWith("wg", StringComparison.OrdinalIgnoreCase) ||
                                      ni.Name.StartsWith("tap", StringComparison.OrdinalIgnoreCase) ||
                                      ni.Name.StartsWith("utun", StringComparison.OrdinalIgnoreCase);

            if (isVpnOrPointToPoint)
            {
                var unicast = props.UnicastAddresses
                    .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork &&
                                         !IPAddress.IsLoopback(u.Address) &&
                                         !Equals(u.Address, IPAddress.Any));

                if (unicast != null)
                {
                    var bytes = unicast.Address.GetAddressBytes();
                    if (bytes[3] != 1)
                    {
                        var gwBytes = (byte[])bytes.Clone();
                        gwBytes[3] = 1;
                        return new IPAddress(gwBytes);
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private string GetEffectiveBoundInterface()
    {
        var iface = this.boundInterface;
        if (string.IsNullOrWhiteSpace(iface))
        {
            iface = this.configService?.BindInterface;
        }

        if (string.IsNullOrWhiteSpace(iface))
        {
            iface = this.configService?.NetworkInterfaceBinding;
        }

        return iface;
    }

    private IPAddress ResolveGateway(IPAddress explicitGateway = null)
    {
        return explicitGateway ?? DiscoverDefaultGateway(this.GetEffectiveBoundInterface());
    }

    public async Task<IPAddress> GetExternalIpAddressAsync(IPAddress gateway = null, CancellationToken cancellationToken = default)
    {
        var targetGateway = this.ResolveGateway(gateway);
        if (targetGateway == null)
        {
            return null;
        }

        // RFC 6886 external address request: 2 bytes [0x00, 0x00]
        var request = new byte[] { 0x00, 0x00 };
        var buffer = await this.SendAndReceiveWithRetryAsync(targetGateway, request, expectedResponseOpcode: 0x80, maxAttempts: 3, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (buffer != null && buffer.Length >= 12 && buffer[0] == 0x00 && buffer[1] == 0x80)
        {
            var resultCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
            if (resultCode == 0)
            {
                var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4, 4));
                this.TrackEpoch(epoch);

                var ipBytes = buffer.AsSpan(8, 4).ToArray();
                return new IPAddress(ipBytes);
            }
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
        var targetGateway = this.ResolveGateway(gateway);
        if (targetGateway == null)
        {
            return new NatPmpMappingResult
            {
                Success = false,
                InternalPort = internalPort,
                ErrorMessage = "No IPv4 default gateway found for NAT-PMP.",
            };
        }

        var result = await this.SendMappingRequestAsync(
            internalPort,
            protocol,
            suggestedExternalPort,
            lifetimeSeconds,
            targetGateway,
            cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            if (lifetimeSeconds > 0)
            {
                if (Interlocked.CompareExchange(ref this.isRunning, 1, 0) == 0)
                {
                    this.renewalTimer?.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                }

                var renewalDelaySeconds = Math.Max(30, result.LifetimeSeconds / 2);
                var active = new ActivePortMapping
                {
                    InternalPort = result.InternalPort,
                    Protocol = protocol,
                    ExternalPort = result.ExternalPort,
                    LifetimeSeconds = result.LifetimeSeconds,
                    GatewayAddress = targetGateway,
                    LastEpoch = this.lastObservedEpoch ?? 0,
                    CreatedUtc = DateTime.UtcNow,
                    NextRenewalUtc = DateTime.UtcNow.AddSeconds(renewalDelaySeconds),
                };

                this.activeMappings[(internalPort, protocol)] = active;
                this.lastKnownGateway = targetGateway;
            }
            else
            {
                this.activeMappings.TryRemove((internalPort, protocol), out _);
            }
        }

        return result;
    }

    public async Task<bool> UnmapPortAsync(
        int internalPort,
        NatPmpProtocol protocol,
        IPAddress gateway = null,
        CancellationToken cancellationToken = default)
    {
        var targetGateway = gateway;
        if (targetGateway == null && this.activeMappings.TryGetValue((internalPort, protocol), out var active))
        {
            targetGateway = active.GatewayAddress;
        }

        var result = await this.MapPortAsync(internalPort, protocol, 0, lifetimeSeconds: 0, targetGateway, cancellationToken).ConfigureAwait(false);
        this.activeMappings.TryRemove((internalPort, protocol), out _);
        return result.Success;
    }

    public async Task RenewAllMappingsAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (this.activeMappings.IsEmpty)
        {
            return;
        }

        await this.renewalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var currentGateway = this.ResolveGateway();

            foreach (var kvp in this.activeMappings)
            {
                var mapping = kvp.Value;
                if (force || now >= mapping.NextRenewalUtc)
                {
                    var targetGateway = mapping.GatewayAddress ?? currentGateway;
                    await this.RenewMappingInternalAsync(mapping, targetGateway, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            this.renewalLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref this.isRunning, 0) == 0)
        {
            return;
        }

        this.renewalTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        var mappingsToRevoke = this.activeMappings.Values.ToList();
        this.activeMappings.Clear();

        foreach (var mapping in mappingsToRevoke)
        {
            try
            {
                var targetGateway = mapping.GatewayAddress ?? this.ResolveGateway();
                if (targetGateway != null)
                {
                    await this.SendMappingRequestAsync(
                        mapping.InternalPort,
                        mapping.Protocol,
                        suggestedExternalPort: 0,
                        lifetimeSeconds: 0,
                        targetGateway,
                        cancellationToken).ConfigureAwait(false);

                    this.logger.Info(
                        "Revoked NAT-PMP port mapping on shutdown: {0} {1} via {2}",
                        mapping.Protocol,
                        mapping.InternalPort,
                        targetGateway);
                }
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Error revoking NAT-PMP mapping for {0} {1}", mapping.Protocol, mapping.InternalPort);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(1000);
            this.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
        }

        this.renewalTimer?.Dispose();
        this.renewalLock?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(2000);
            await this.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
        }

        this.renewalTimer?.Dispose();
        this.renewalLock?.Dispose();
    }

    private async Task CheckAndRenewMappingsAsync()
    {
        if (this.isRunning == 0 || this.activeMappings.IsEmpty)
        {
            return;
        }

        if (!await this.renewalLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            var now = DateTime.UtcNow;
            var currentGateway = this.ResolveGateway();

            var gatewayChanged = currentGateway != null &&
                                 this.lastKnownGateway != null &&
                                 !currentGateway.Equals(this.lastKnownGateway);

            if (currentGateway != null)
            {
                this.lastKnownGateway = currentGateway;
            }

            foreach (var kvp in this.activeMappings)
            {
                var mapping = kvp.Value;
                var targetGateway = gatewayChanged ? currentGateway : (mapping.GatewayAddress ?? currentGateway);
                if (now >= mapping.NextRenewalUtc || gatewayChanged)
                {
                    await this.RenewMappingInternalAsync(mapping, targetGateway, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error occurred during NAT-PMP lease renewal check");
        }
        finally
        {
            this.renewalLock.Release();
        }
    }

    private async Task<bool> RenewMappingInternalAsync(
        ActivePortMapping mapping,
        IPAddress gateway,
        CancellationToken cancellationToken)
    {
        var targetGateway = gateway ?? mapping.GatewayAddress ?? this.ResolveGateway();
        if (targetGateway == null)
        {
            this.logger.Warn("Cannot renew NAT-PMP mapping for {0} {1}: No gateway found.", mapping.Protocol, mapping.InternalPort);
            mapping.NextRenewalUtc = DateTime.UtcNow.AddSeconds(60);
            return false;
        }

        var result = await this.SendMappingRequestAsync(
            mapping.InternalPort,
            mapping.Protocol,
            suggestedExternalPort: mapping.ExternalPort,
            lifetimeSeconds: mapping.LifetimeSeconds > 0 ? mapping.LifetimeSeconds : 3600,
            targetGateway,
            cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            mapping.ExternalPort = result.ExternalPort;
            mapping.LifetimeSeconds = result.LifetimeSeconds;
            mapping.GatewayAddress = result.GatewayAddress;
            var renewalDelay = Math.Max(30, result.LifetimeSeconds / 2);
            mapping.NextRenewalUtc = DateTime.UtcNow.AddSeconds(renewalDelay);

            this.logger.Info(
                "NAT-PMP port mapping renewed for {0} {1} -> {2} (Lifetime: {3}s, Next renewal in {4}s)",
                mapping.Protocol,
                mapping.InternalPort,
                mapping.ExternalPort,
                result.LifetimeSeconds,
                renewalDelay);

            return true;
        }
        else
        {
            this.logger.Warn(
                "NAT-PMP port mapping renewal failed for {0} {1}: {2}. Will retry.",
                mapping.Protocol,
                mapping.InternalPort,
                result.ErrorMessage);

            // Retry after 60 seconds on failure
            mapping.NextRenewalUtc = DateTime.UtcNow.AddSeconds(60);
            return false;
        }
    }

    private async Task<NatPmpMappingResult> SendMappingRequestAsync(
        int internalPort,
        NatPmpProtocol protocol,
        int suggestedExternalPort,
        int lifetimeSeconds,
        IPAddress targetGateway,
        CancellationToken cancellationToken)
    {
        // RFC 6886 mapping request: 12 bytes
        // [0x00, opcode(1=udp, 2=tcp), reserved(2), internalPort(2), suggestedExternalPort(2), lifetime(4)]
        var request = new byte[12];
        request[0] = 0x00;
        request[1] = (byte)protocol;
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), (ushort)internalPort);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(6, 2), (ushort)(suggestedExternalPort > 0 ? suggestedExternalPort : (lifetimeSeconds > 0 ? internalPort : 0)));
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(8, 4), (uint)Math.Max(0, lifetimeSeconds));

        var expectedOpcode = (byte)(0x80 + (byte)protocol);
        var buffer = await this.SendAndReceiveWithRetryAsync(
            targetGateway,
            request,
            expectedOpcode,
            maxAttempts: 3,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (buffer != null && buffer.Length >= 16 && buffer[0] == 0x00 && buffer[1] == expectedOpcode)
        {
            var resultCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
            var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4, 4));
            this.TrackEpoch(epoch);

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

        return new NatPmpMappingResult
        {
            Success = false,
            InternalPort = internalPort,
            ErrorMessage = $"NAT-PMP did not receive a response from gateway {targetGateway}.",
        };
    }

    private async Task<byte[]> SendAndReceiveWithRetryAsync(
        IPAddress targetGateway,
        byte[] request,
        byte expectedResponseOpcode,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        var delayMs = 250;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            UdpClient udp = null;
            try
            {
                udp = new UdpClient();
                var endpoint = new IPEndPoint(targetGateway, this.gatewayPort);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(Math.Min(delayMs * 2, 2000));

                await udp.SendAsync(request, request.Length, endpoint).ConfigureAwait(false);
                var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                var buffer = result.Buffer;

                if (buffer.Length >= 2 && buffer[0] == 0x00 && buffer[1] == expectedResponseOpcode)
                {
                    return buffer;
                }
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
            catch (SocketException ex)
            {
                this.logger.Debug(ex, "NAT-PMP socket error on attempt {0}/{1}", attempt, maxAttempts);
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "NAT-PMP unexpected error on attempt {0}/{1}", attempt, maxAttempts);
            }
            finally
            {
                try
                {
                    udp?.Dispose();
                }
                catch
                {
                }
            }

            if (attempt < maxAttempts)
            {
                try
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }

                delayMs *= 2;
            }
        }

        return null;
    }

    private void TrackEpoch(uint epoch)
    {
        if (this.lastObservedEpoch.HasValue && epoch < this.lastObservedEpoch.Value)
        {
            this.logger.Warn(
                "NAT-PMP gateway epoch decreased from {0} to {1} (gateway reboot detected).",
                this.lastObservedEpoch.Value,
                epoch);

            _ = Task.Run(async () =>
            {
                try
                {
                    await this.RenewAllMappingsAsync(force: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    this.logger.Error(ex, "Error re-creating NAT-PMP mappings after gateway reboot");
                }
            });
        }

        this.lastObservedEpoch = epoch;
    }
}
