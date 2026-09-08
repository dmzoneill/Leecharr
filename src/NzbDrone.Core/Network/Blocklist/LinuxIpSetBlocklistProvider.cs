// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
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
    private bool isKernelOffloadActive;

    public string ProviderId => "LinuxIpSet";

    public string DisplayName => "Linux Kernel IPSet / Netfilter Drop";

    public string Version => "1.0";

    public bool IsAvailable => OsInfo.IsLinux && this.HasIpSetBinary();

    public BlocklistCapabilities Capabilities => BlocklistCapabilities.IPv4 | BlocklistCapabilities.IPv6 | BlocklistCapabilities.Cidr | BlocklistCapabilities.LinuxIpSet;

    public int RuleCount => this.inMemoryTrie.RuleCount;

    public bool IsKernelOffloadActive => this.isKernelOffloadActive;

    public LinuxIpSetBlocklistProvider(IDiskProvider diskProvider)
    {
        this.diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public async Task<BlocklistHealthResult> ProbeHealthAsync()
    {
        if (!OsInfo.IsLinux)
        {
            return new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = "Linux IPSet provider requires a Linux operating system.",
                LoadedRuleCount = this.RuleCount,
                Warnings = new List<string> { "Non-Linux OS detected. IPSet kernel offload disabled." },
            };
        }

        var ipSetPath = this.GetIpSetBinaryPath();
        if (string.IsNullOrEmpty(ipSetPath))
        {
            return new BlocklistHealthResult
            {
                IsHealthy = false,
                StatusMessage = "ipset binary not found in standard system paths (/usr/sbin/ipset, /sbin/ipset, /usr/bin/ipset).",
                LoadedRuleCount = this.RuleCount,
                Warnings = new List<string> { "Missing 'ipset' package or binary." },
            };
        }

        try
        {
            var (exitCode, stdout, stderr) = await this.ExecuteIpSetCommandAsync("list -n").ConfigureAwait(false);
            if (exitCode == 0)
            {
                return new BlocklistHealthResult
                {
                    IsHealthy = true,
                    StatusMessage = $"Linux IPSet provider operational with kernel netfilter offload enabled ({ipSetPath}) and {this.RuleCount} rules loaded.",
                    LoadedRuleCount = this.RuleCount,
                };
            }

            return new BlocklistHealthResult
            {
                IsHealthy = true,
                StatusMessage = $"Linux IPSet provider operational in user-space fallback mode ({ipSetPath}) with {this.RuleCount} rules loaded.",
                LoadedRuleCount = this.RuleCount,
                Warnings = new List<string>
                {
                    $"Kernel IPSet capability check failed (exit code {exitCode}: {stderr.Trim()}). Ensure CAP_NET_ADMIN permissions or root privileges. Rules will be evaluated in-memory via Radix tree.",
                },
            };
        }
        catch (Exception ex)
        {
            return new BlocklistHealthResult
            {
                IsHealthy = true,
                StatusMessage = $"Linux IPSet provider operational in user-space fallback mode ({ipSetPath}) with {this.RuleCount} rules loaded.",
                LoadedRuleCount = this.RuleCount,
                Warnings = new List<string>
                {
                    $"Failed to probe kernel IPSet capability: {ex.Message}. Falling back to in-memory Radix tree.",
                },
            };
        }
    }

    public bool IsIpBlocked(string ipAddress)
    {
        return this.inMemoryTrie.IsIpBlocked(ipAddress);
    }

    public async Task<int> LoadRulesAsync(IEnumerable<string> rules)
    {
        var ruleList = rules?.ToList() ?? new List<string>();
        var loadedCount = await this.inMemoryTrie.LoadRulesAsync(ruleList).ConfigureAwait(false);

        if (OsInfo.IsLinux && this.HasIpSetBinary())
        {
            try
            {
                var restoreScript = this.BuildIpSetRestoreScript(ruleList);
                var (exitCode, _, stderr) = await this.ExecuteIpSetCommandAsync("restore", restoreScript).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    this.isKernelOffloadActive = true;
                    this.logger.Info("Successfully applied {0} blocklist rules to Linux kernel ipsets (leecharr_blocklist_v4, leecharr_blocklist_v6).", loadedCount);
                }
                else
                {
                    this.isKernelOffloadActive = false;
                    this.logger.Warn("Failed to apply rules to kernel ipset (exit code {0}: {1}). Falling back to user-space Radix tree.", exitCode, stderr.Trim());
                }
            }
            catch (Exception ex)
            {
                this.isKernelOffloadActive = false;
                this.logger.Warn(ex, "Exception while synchronizing rules with kernel ipset. Operating in user-space fallback mode.");
            }
        }

        return loadedCount;
    }

    public void ClearRules()
    {
        this.inMemoryTrie.ClearRules();
        this.isKernelOffloadActive = false;

        if (OsInfo.IsLinux && this.HasIpSetBinary())
        {
            try
            {
                _ = this.ExecuteIpSetCommandAsync("flush leecharr_blocklist_v4");
                _ = this.ExecuteIpSetCommandAsync("flush leecharr_blocklist_v6");
            }
            catch (Exception ex)
            {
                this.logger.Debug(ex, "Failed to flush kernel ipsets during ClearRules.");
            }
        }
    }

    protected virtual Task<(int ExitCode, string StdOut, string StdErr)> ExecuteIpSetCommandAsync(
        string arguments,
        string stdIn = null,
        CancellationToken cancellationToken = default)
    {
        var binaryPath = this.GetIpSetBinaryPath();
        if (string.IsNullOrEmpty(binaryPath))
        {
            return Task.FromResult((-1, string.Empty, "ipset binary not found"));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = arguments,
            RedirectStandardInput = stdIn != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();

            if (stdIn != null)
            {
                using (var writer = process.StandardInput)
                {
                    writer.Write(stdIn);
                    writer.Flush();
                }
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return Task.FromResult((process.ExitCode, stdout, stderr));
        }
        catch (Exception ex)
        {
            return Task.FromResult((-1, string.Empty, ex.Message));
        }
    }

    private string BuildIpSetRestoreScript(IEnumerable<string> rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("create leecharr_tmp_v4 hash:net family inet maxelem 1000000 -exist");
        sb.AppendLine("flush leecharr_tmp_v4");
        sb.AppendLine("create leecharr_tmp_v6 hash:net family inet6 maxelem 1000000 -exist");
        sb.AppendLine("flush leecharr_tmp_v6");

        foreach (var rule in rules)
        {
            var cleaned = CleanRule(rule);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                continue;
            }

            if (TryParseCidrOrIp(cleaned, out var formatted, out var isIPv6))
            {
                if (isIPv6)
                {
                    sb.AppendLine($"add leecharr_tmp_v6 {formatted} -exist");
                }
                else
                {
                    sb.AppendLine($"add leecharr_tmp_v4 {formatted} -exist");
                }
            }
        }

        sb.AppendLine("create leecharr_blocklist_v4 hash:net family inet maxelem 1000000 -exist");
        sb.AppendLine("create leecharr_blocklist_v6 hash:net family inet6 maxelem 1000000 -exist");
        sb.AppendLine("swap leecharr_tmp_v4 leecharr_blocklist_v4");
        sb.AppendLine("swap leecharr_tmp_v6 leecharr_blocklist_v6");
        sb.AppendLine("destroy leecharr_tmp_v4");
        sb.AppendLine("destroy leecharr_tmp_v6");

        return sb.ToString();
    }

    private static string CleanRule(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return null;
        }

        var text = rule.Trim();

        // Strip inline comments
        var commentIdx = text.IndexOfAny(new[] { '#', ';' });
        if (commentIdx >= 0)
        {
            text = text.Substring(0, commentIdx).Trim();
        }

        var slashCommentIdx = text.IndexOf("//", StringComparison.Ordinal);
        if (slashCommentIdx >= 0)
        {
            text = text.Substring(0, slashCommentIdx).Trim();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // Handle P2P header prefixes (e.g. "Spamhaus:1.2.3.4/32" or "ISP:1.2.3.4:0")
        var lastColon = text.LastIndexOf(':');
        var firstColon = text.IndexOf(':');
        if (firstColon >= 0 && !text.Contains("::") && IPAddress.TryParse(text, out _))
        {
            // It's a standard IPv6 address
        }
        else if (firstColon >= 0 && !text.Contains("::"))
        {
            // Colon header or level suffix
            var parts = text.Split(':');
            foreach (var part in parts)
            {
                var candidate = part.Trim();
                if (IPAddress.TryParse(candidate, out _) || candidate.Contains('/'))
                {
                    text = candidate;
                    break;
                }
            }
        }

        return text;
    }

    private static bool TryParseCidrOrIp(string input, out string formatted, out bool isIPv6)
    {
        formatted = null;
        isIPv6 = false;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var slashIdx = input.IndexOf('/');
        if (slashIdx >= 0)
        {
            var ipPart = input.Substring(0, slashIdx).Trim();
            var prefixPart = input.Substring(slashIdx + 1).Trim();

            if (IPAddress.TryParse(ipPart, out var ip) && int.TryParse(prefixPart, out var prefix))
            {
                if (ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();
                    prefix = Math.Clamp(prefix - 96, 0, 32);
                }

                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    formatted = $"{ip}/{prefix}";
                    isIPv6 = false;
                    return true;
                }

                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                {
                    formatted = $"{ip}/{prefix}";
                    isIPv6 = true;
                    return true;
                }
            }
        }
        else if (IPAddress.TryParse(input, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                formatted = $"{ip}/32";
                isIPv6 = false;
                return true;
            }

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                formatted = $"{ip}/128";
                isIPv6 = true;
                return true;
            }
        }

        return false;
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
