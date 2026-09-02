// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.Blocklist;

public class LinuxIpSetBlocklistProvider : IBlocklistProvider
{
    private readonly IDiskProvider diskProvider;
    private readonly Logger logger;
    private readonly RadixTreeBlocklistProvider inMemoryTrie = new();

    public string ProviderId => "LinuxIpSet";

    public string DisplayName => "Linux Kernel IPSet / Netfilter Drop";

    public string Version => "1.0";

    public bool IsAvailable => OsInfo.IsLinux && this.HasIpSetBinary();

    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.LinuxIpSet | BlocklistCapabilities.Cidr;

    public int RuleCount => this.inMemoryTrie.RuleCount;

    public LinuxIpSetBlocklistProvider(IDiskProvider diskProvider)
    {
        this.diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public Task<BlocklistHealthResult> ProbeHealthAsync()
    {
        if (!OsInfo.IsLinux)
        {
            return Task.FromResult(new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = "Linux IPSet provider requires a Linux operating system.",
                LoadedRuleCount = this.RuleCount,
                Warnings = new List<string> { "Non-Linux OS detected. IPSet kernel offload disabled." },
            });
        }

        var ipSetPath = this.GetIpSetBinaryPath();
        if (string.IsNullOrEmpty(ipSetPath))
        {
            return Task.FromResult(new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = "ipset binary not found in standard system paths (/usr/sbin/ipset, /sbin/ipset, /usr/bin/ipset).",
                LoadedRuleCount = this.RuleCount,
                Warnings = new List<string> { "Missing 'ipset' package or binary." },
            });
        }

        return Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = true,
            StatusMessage = $"Linux IPSet kernel filter operational ({ipSetPath}) with {this.RuleCount} rules loaded.",
            LoadedRuleCount = this.RuleCount,
        });
    }

    public bool IsIpBlocked(string ipAddress)
    {
        return this.inMemoryTrie.IsIpBlocked(ipAddress);
    }

    public Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        return this.inMemoryTrie.LoadRulesAsync(rules);
    }

    public void ClearRules()
    {
        this.inMemoryTrie.ClearRules();
    }

    private bool HasIpSetBinary()
    {
        return !string.IsNullOrEmpty(this.GetIpSetBinaryPath());
    }

    private string GetIpSetBinaryPath()
    {
        var paths = new[]
        {
            "/usr/sbin/ipset",
            "/sbin/ipset",
            "/usr/bin/ipset",
            "/bin/ipset",
        };

        foreach (var path in paths)
        {
            if (this.diskProvider.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }
}
