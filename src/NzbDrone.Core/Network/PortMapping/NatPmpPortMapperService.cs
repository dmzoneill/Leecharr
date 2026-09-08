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

    private readonly ConcurrentDictionary<IPAddress, uint> gatewayEpochs = new();
    private int isRunning = 1;
    private int isDisposed;
    private int isForceRenewalRunning;
    private int rebootRenewalScheduled;
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

    internal IReadOnlyDictionary<IPAddress, uint> GatewayEpochs => this.gatewayEpochs;

    private static readonly string[] VirtualInterfacePatterns =
    [
        "docker",
        "veth",
        "vmnet",
        "vboxnet",
        "wsl",
        "hyper-v",
        "vethernet",
        "virbr",
        "vmware",
        "virtualbox",
        "cni",
        "flannel",
        "calico",
        "tailscale",
        "zerotier",
    ];

    public static IPAddress DiscoverDefaultGateway(string boundInterface = null)
    {
        try
        {
            var candidates = GetSystemNetworkCandidates();
            return SelectBestGateway(candidates, boundInterface);
        }
        catch
        {
            return null;
        }
    }

    public static IPAddress SelectBestGateway(IEnumerable<NatPmpNetworkInterfaceCandidate> candidates, string boundInterface = null)
    {
        if (candidates == null)
        {
            return null;
        }

        var candidateList = candidates.ToList();

        // 1. If a specific bound interface is provided and active, prioritize its gateway
        if (!string.IsNullOrWhiteSpace(boundInterface) &&
            !string.Equals(boundInterface, "any", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(boundInterface, "all", StringComparison.OrdinalIgnoreCase))
        {
            var boundNic = candidateList.FirstOrDefault(n =>
                string.Equals(n.Name, boundInterface, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Id, boundInterface, StringComparison.OrdinalIgnoreCase));

            if (boundNic != null && boundNic.GatewayAddresses.Count > 0)
            {
                var matchedGw = boundNic.GatewayAddresses.FirstOrDefault(gw =>
                    boundNic.UnicastAddresses.Any(u => u.Mask != null && IsInSameSubnet(u.Address, gw, u.Mask)));

                return matchedGw ?? boundNic.GatewayAddresses[0];
            }
        }

        // 2. Physical interfaces: Ethernet, Wireless80211 without virtual description/name patterns
        // Filter out Tunnel, PPP, Loopback, and down interfaces
        // Verify candidate interfaces have an active non-APIPA IPv4 unicast address
        var physicalCandidates = candidateList
            .Where(c => c.OperationalStatus == OperationalStatus.Up &&
                        c.InterfaceType != NetworkInterfaceType.Loopback &&
                        c.InterfaceType != NetworkInterfaceType.Tunnel &&
                        c.InterfaceType != NetworkInterfaceType.Ppp &&
                        IsPhysicalInterfaceType(c.InterfaceType) &&
                        !IsVirtualInterfaceName(c.Name, c.Description) &&
                        c.UnicastAddresses.Any(u => IsValidIpv4UnicastAddress(u.Address)) &&
                        c.GatewayAddresses.Any(IsValidGatewayAddress))
            .ToList();

        if (physicalCandidates.Count > 0)
        {
            // First look for physical candidates where the gateway matches the unicast subnet
            foreach (var physical in physicalCandidates)
            {
                var subnetMatchedGateway = physical.GatewayAddresses
                    .FirstOrDefault(gw => physical.UnicastAddresses.Any(u => u.Mask != null && IsInSameSubnet(u.Address, gw, u.Mask)));

                if (subnetMatchedGateway != null)
                {
                    return subnetMatchedGateway;
                }
            }

            // Otherwise return the first valid gateway on the first physical adapter
            return physicalCandidates[0].GatewayAddresses.First(IsValidGatewayAddress);
        }

        // 3. Fallback to other candidate interfaces (virtual bridges, VPNs, etc.)
        // Filter out tunnel/loopback/down unless valid gateway exists; deprioritize Docker default bridge 172.17.0.1
        var fallbackCandidates = candidateList
            .Where(c => c.OperationalStatus == OperationalStatus.Up &&
                        c.InterfaceType != NetworkInterfaceType.Loopback &&
                        c.UnicastAddresses.Any(u => IsValidIpv4UnicastAddress(u.Address)) &&
                        c.GatewayAddresses.Any(IsValidGatewayAddress))
            .ToList();

        IPAddress dockerBridgeGateway = null;
        foreach (var fallback in fallbackCandidates)
        {
            foreach (var gw in fallback.GatewayAddresses.Where(IsValidGatewayAddress))
            {
                if (gw.ToString() == "172.17.0.1" || fallback.Name.Contains("docker", StringComparison.OrdinalIgnoreCase))
                {
                    dockerBridgeGateway ??= gw;
                }
                else
                {
                    return gw;
                }
            }
        }

        return dockerBridgeGateway;
    }

    public static bool IsVirtualInterfaceName(string name, string description = null)
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        foreach (var pattern in VirtualInterfacePatterns)
        {
            if (!string.IsNullOrEmpty(name) && name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(description) && description.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsPhysicalInterfaceType(NetworkInterfaceType type)
    {
        return type == NetworkInterfaceType.Ethernet ||
               type == NetworkInterfaceType.Wireless80211 ||
               type == NetworkInterfaceType.GigabitEthernet ||
               type == NetworkInterfaceType.FastEthernetFx ||
               type == NetworkInterfaceType.FastEthernetT;
    }

    public static bool IsValidIpv4UnicastAddress(IPAddress address)
    {
        if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address) || Equals(address, IPAddress.Any) || Equals(address, IPAddress.None))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        // Filter out 0.0.0.0
        if (bytes[0] == 0)
        {
            return false;
        }

        // Filter out APIPA (169.254.0.0/16)
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return false;
        }

        return true;
    }

    public static bool IsValidGatewayAddress(IPAddress address)
    {
        if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        if (IPAddress.IsLoopback(address) || Equals(address, IPAddress.Any) || Equals(address, IPAddress.None))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();

        // 0.0.0.0 is not a valid gateway
        if (bytes[0] == 0)
        {
            return false;
        }

        // 169.254.x.x APIPA is not a valid gateway
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return false;
        }

        return true;
    }

    public static bool IsInSameSubnet(IPAddress ip, IPAddress gateway, IPAddress mask)
    {
        if (ip == null || gateway == null || mask == null)
        {
            return false;
        }

        if (ip.AddressFamily != AddressFamily.InterNetwork ||
            gateway.AddressFamily != AddressFamily.InterNetwork ||
            mask.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var ipBytes = ip.GetAddressBytes();
        var gwBytes = gateway.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        // Ignore invalid masks like 0.0.0.0 or 255.255.255.255
        if ((maskBytes[0] == 0 && maskBytes[1] == 0 && maskBytes[2] == 0 && maskBytes[3] == 0) ||
            (maskBytes[0] == 255 && maskBytes[1] == 255 && maskBytes[2] == 255 && maskBytes[3] == 255))
        {
            return false;
        }

        var ipInt = BinaryPrimitives.ReadUInt32BigEndian(ipBytes);
        var gwInt = BinaryPrimitives.ReadUInt32BigEndian(gwBytes);
        var maskInt = BinaryPrimitives.ReadUInt32BigEndian(maskBytes);

        return (ipInt & maskInt) == (gwInt & maskInt);
    }

    public static bool IsVpnInterfaceName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.StartsWith("tun", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("wg", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("ppp", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("tap", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("vpn", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("utun", StringComparison.OrdinalIgnoreCase);
    }

    public static IPAddress GetGatewayFromInterface(NetworkInterface ni)
    {
        try
        {
            var props = ni.GetIPProperties();
            var gw = props.GatewayAddresses
                .FirstOrDefault(g => IsValidGatewayAddress(g.Address));

            if (gw != null)
            {
                return gw.Address;
            }

            // For VPN / point-to-point interfaces where GatewayAddresses is empty in Linux,
            // check if there is a unicast IPv4 address and resolve the default subnet gateway (e.g. .1)
            var isVpnOrPointToPoint = ni.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                                      ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                                      IsVpnInterfaceName(ni.Name);

            if (isVpnOrPointToPoint)
            {
                var unicast = props.UnicastAddresses
                    .FirstOrDefault(u => IsValidIpv4UnicastAddress(u.Address));

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

    private static List<NatPmpNetworkInterfaceCandidate> GetSystemNetworkCandidates()
    {
        var candidates = new List<NatPmpNetworkInterfaceCandidate>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }

                    var props = ni.GetIPProperties();
                    var unicasts = new List<(IPAddress Address, IPAddress Mask)>();
                    if (props.UnicastAddresses != null)
                    {
                        foreach (var u in props.UnicastAddresses)
                        {
                            if (u.Address != null && IsValidIpv4UnicastAddress(u.Address))
                            {
                                unicasts.Add((u.Address, u.IPv4Mask));
                            }
                        }
                    }

                    var gateways = new List<IPAddress>();
                    if (props.GatewayAddresses != null)
                    {
                        foreach (var gw in props.GatewayAddresses)
                        {
                            if (gw.Address != null && IsValidGatewayAddress(gw.Address))
                            {
                                gateways.Add(gw.Address);
                            }
                        }
                    }

                    // For VPN / point-to-point interfaces where GatewayAddresses is empty in Linux,
                    // check if there is a unicast IPv4 address and resolve default subnet gateway (.1)
                    if (gateways.Count == 0 && unicasts.Count > 0)
                    {
                        var isVpnOrPointToPoint = ni.NetworkInterfaceType == NetworkInterfaceType.Ppp ||
                                                  ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                                                  IsVpnInterfaceName(ni.Name);

                        if (isVpnOrPointToPoint)
                        {
                            var bytes = unicasts[0].Address.GetAddressBytes();
                            if (bytes[3] != 1)
                            {
                                var gwBytes = (byte[])bytes.Clone();
                                gwBytes[3] = 1;
                                gateways.Add(new IPAddress(gwBytes));
                            }
                        }
                    }

                    candidates.Add(new NatPmpNetworkInterfaceCandidate(
                        ni.Name,
                        ni.Description,
                        ni.Id,
                        ni.NetworkInterfaceType,
                        ni.OperationalStatus,
                        unicasts,
                        gateways));
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return candidates;
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
        ObjectDisposedException.ThrowIf(this.isDisposed != 0, this);

        var targetGateway = this.ResolveGateway(gateway);
        if (targetGateway == null || targetGateway.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

        // RFC 6886 external address request: 2 bytes [0x00, 0x00]
        var request = new byte[] { 0x00, 0x00 };
        var buffer = await this.SendAndReceiveWithRetryAsync(targetGateway, request, expectedResponseOpcode: 0x80, maxAttempts: 3, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (buffer != null && buffer.Length >= 8 && buffer[0] == 0x00 && buffer[1] == 0x80)
        {
            var resultCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
            var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4, 4));
            this.TrackEpoch(targetGateway, epoch);

            if (resultCode == 0 && buffer.Length >= 12)
            {
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
        ObjectDisposedException.ThrowIf(this.isDisposed != 0, this);

        var targetGateway = this.ResolveGateway(gateway);
        if (targetGateway == null || targetGateway.AddressFamily != AddressFamily.InterNetwork)
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

        if (this.isDisposed != 0)
        {
            throw new ObjectDisposedException(nameof(NatPmpPortMapperService));
        }

        if (result.Success)
        {
            if (lifetimeSeconds > 0)
            {
                if (this.isDisposed == 0 && Interlocked.CompareExchange(ref this.isRunning, 1, 0) == 0)
                {
                    try
                    {
                        this.renewalTimer?.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                var renewalDelaySeconds = Math.Max(30, result.LifetimeSeconds / 2);
                var active = new ActivePortMapping
                {
                    InternalPort = result.InternalPort,
                    Protocol = protocol,
                    ExternalPort = result.ExternalPort,
                    LifetimeSeconds = result.LifetimeSeconds,
                    GatewayAddress = targetGateway,
                    LastEpoch = targetGateway != null && this.gatewayEpochs.TryGetValue(targetGateway, out var gwEpoch) ? gwEpoch : 0,
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
        ObjectDisposedException.ThrowIf(this.isDisposed != 0, this);

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
        ObjectDisposedException.ThrowIf(this.isDisposed != 0, this);

        if (this.activeMappings.IsEmpty)
        {
            return;
        }

        try
        {
            await this.renewalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            if (this.isDisposed != 0)
            {
                throw new ObjectDisposedException(nameof(NatPmpPortMapperService));
            }

            throw;
        }

        if (force)
        {
            Interlocked.Exchange(ref this.isForceRenewalRunning, 1);
        }

        try
        {
            ObjectDisposedException.ThrowIf(this.isDisposed != 0, this);

            var now = DateTime.UtcNow;
            var currentGateway = this.ResolveGateway();

            foreach (var kvp in this.activeMappings)
            {
                if (this.isDisposed != 0)
                {
                    break;
                }

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
            if (force)
            {
                Interlocked.Exchange(ref this.isForceRenewalRunning, 0);
            }

            try
            {
                this.renewalLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref this.isRunning, 0) == 0)
        {
            return;
        }

        try
        {
            this.renewalTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }

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
            this.renewalTimer?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            using var cts = new CancellationTokenSource(1000);
            this.StopAsync(cts.Token).GetAwaiter().GetResult();
        }
        catch
        {
        }

        try
        {
            this.renewalLock?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            this.renewalTimer?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            using var cts = new CancellationTokenSource(2000);
            await this.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch
        {
        }

        try
        {
            this.renewalLock?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task CheckAndRenewMappingsAsync()
    {
        if (this.isDisposed != 0 || this.isRunning == 0 || this.activeMappings.IsEmpty)
        {
            return;
        }

        try
        {
            if (!await this.renewalLock.WaitAsync(0).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (this.isDisposed != 0 || this.isRunning == 0)
            {
                return;
            }

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
                if (this.isDisposed != 0 || this.isRunning == 0)
                {
                    break;
                }

                var mapping = kvp.Value;
                var targetGateway = gatewayChanged ? currentGateway : (mapping.GatewayAddress ?? currentGateway);
                if (now >= mapping.NextRenewalUtc || gatewayChanged)
                {
                    await this.RenewMappingInternalAsync(mapping, targetGateway, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Error occurred during NAT-PMP lease renewal check");
        }
        finally
        {
            try
            {
                this.renewalLock.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task<bool> RenewMappingInternalAsync(
        ActivePortMapping mapping,
        IPAddress gateway,
        CancellationToken cancellationToken)
    {
        var targetGateway = gateway ?? mapping.GatewayAddress ?? this.ResolveGateway();
        if (targetGateway == null || targetGateway.AddressFamily != AddressFamily.InterNetwork)
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
            if (result.GatewayAddress != null && this.gatewayEpochs.TryGetValue(result.GatewayAddress, out var ep))
            {
                mapping.LastEpoch = ep;
            }

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

        if (buffer != null && buffer.Length >= 8 && buffer[0] == 0x00 && buffer[1] == expectedOpcode)
        {
            var resultCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2, 2));
            var epoch = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4, 4));
            this.TrackEpoch(targetGateway, epoch);

            if (resultCode != 0)
            {
                return new NatPmpMappingResult
                {
                    Success = false,
                    InternalPort = internalPort,
                    ErrorMessage = $"NAT-PMP gateway returned error code: {resultCode}.",
                };
            }

            if (buffer.Length >= 16)
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
        if (targetGateway == null || targetGateway.AddressFamily != AddressFamily.InterNetwork)
        {
            return null;
        }

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
                udp = new UdpClient(AddressFamily.InterNetwork);
                var endpoint = new IPEndPoint(targetGateway, this.gatewayPort);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(Math.Min(delayMs * 2, 2000));

                await udp.SendAsync(request, request.Length, endpoint).ConfigureAwait(false);

                while (!cts.IsCancellationRequested)
                {
                    var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    if (!targetGateway.Equals(result.RemoteEndPoint.Address) || result.RemoteEndPoint.Port != this.gatewayPort)
                    {
                        continue;
                    }

                    var buffer = result.Buffer;
                    if (buffer.Length >= 2 && buffer[0] == 0x00 && buffer[1] == expectedResponseOpcode)
                    {
                        return buffer;
                    }
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

    internal void TrackEpoch(IPAddress gateway, uint epoch)
    {
        if (this.isDisposed != 0 || gateway == null)
        {
            return;
        }

        var rebootDetected = false;
        uint prevEpoch = 0;

        this.gatewayEpochs.AddOrUpdate(
            gateway,
            epoch,
            (key, oldEpoch) =>
            {
                if (epoch < oldEpoch)
                {
                    rebootDetected = true;
                    prevEpoch = oldEpoch;
                }

                return epoch;
            });

        if (rebootDetected)
        {
            this.logger.Warn(
                "NAT-PMP gateway {0} epoch decreased from {1} to {2} (gateway reboot detected).",
                gateway,
                prevEpoch,
                epoch);

            this.ScheduleRebootRenewal();
        }
    }

    private void ScheduleRebootRenewal()
    {
        if (this.isDisposed != 0 || this.isRunning == 0 || this.isForceRenewalRunning != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref this.rebootRenewalScheduled, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (this.isDisposed != 0 || this.isRunning == 0)
                {
                    return;
                }

                await this.RenewAllMappingsAsync(force: true).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error re-creating NAT-PMP mappings after gateway reboot");
            }
            finally
            {
                Interlocked.Exchange(ref this.rebootRenewalScheduled, 0);
            }
        });
    }
}
