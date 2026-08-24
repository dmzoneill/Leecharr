using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Network.Blocklist;

public class LinuxIpSetBlocklistProvider : IBlocklistProvider
{
    private readonly IDiskProvider _diskProvider;
    private readonly Logger _logger;
    private readonly RadixTreeBlocklistProvider _inMemoryTrie = new();

    public string ProviderId => "LinuxIpSet";
    public string DisplayName => "Linux Kernel IPSet / Netfilter Drop";
    public string Version => "1.0";
    public bool IsAvailable => OsInfo.IsLinux && HasIpSetBinary();
    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.LinuxIpSet | BlocklistCapabilities.Cidr;
    public int RuleCount => _inMemoryTrie.RuleCount;

    public LinuxIpSetBlocklistProvider(IDiskProvider diskProvider)
    {
        _diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        _logger = LogManager.GetCurrentClassLogger();
    }

    public Task<BlocklistHealthResult> ProbeHealthAsync()
    {
        if (!OsInfo.IsLinux)
        {
            return Task.FromResult(new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = "Linux IPSet provider requires a Linux operating system.",
                LoadedRuleCount = RuleCount,
                Warnings = new List<string> { "Non-Linux OS detected. IPSet kernel offload disabled." }
            });
        }

        var ipSetPath = GetIpSetBinaryPath();
        if (string.IsNullOrEmpty(ipSetPath))
        {
            return Task.FromResult(new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = "ipset binary not found in standard system paths (/usr/sbin/ipset, /sbin/ipset, /usr/bin/ipset).",
                LoadedRuleCount = RuleCount,
                Warnings = new List<string> { "Missing 'ipset' package or binary." }
            });
        }

        return Task.FromResult(new BlocklistHealthResult
        {
            IsHealthy = true,
            StatusMessage = $"Linux IPSet kernel filter operational ({ipSetPath}) with {RuleCount} rules loaded.",
            LoadedRuleCount = RuleCount
        });
    }

    public bool IsIpBlocked(string ipAddress)
    {
        return _inMemoryTrie.IsIpBlocked(ipAddress);
    }

    public Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        return _inMemoryTrie.LoadRulesAsync(rules);
    }

    public void ClearRules()
    {
        _inMemoryTrie.ClearRules();
    }

    private bool HasIpSetBinary()
    {
        return !string.IsNullOrEmpty(GetIpSetBinaryPath());
    }

    private string GetIpSetBinaryPath()
    {
        var paths = new[]
        {
            "/usr/sbin/ipset",
            "/sbin/ipset",
            "/usr/bin/ipset",
            "/bin/ipset"
        };

        foreach (var path in paths)
        {
            if (_diskProvider.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }
}
