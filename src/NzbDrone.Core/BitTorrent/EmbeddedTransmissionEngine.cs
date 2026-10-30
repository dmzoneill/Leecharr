// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public class EmbeddedTransmissionEngine : ITorrentEngine, IDisposable, IHandle<VpnKillSwitchTriggeredEvent>, IHandle<VpnInterfaceRestoredEvent>, IHandle<NetworkBindingProviderSwitchedEvent>, IHandle<ConfigSavedEvent>
{
    private static readonly string[] TrackerFields = ["trackers"];
    private static readonly string[] FileFields = ["files"];
    private static readonly string[] PeerFields = ["peers"];

    private readonly IConfigService configService;
    private readonly IStoragePathService storagePathService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly INetworkBindingService networkBindingService;
    private readonly IVpnKillSwitchService vpnKillSwitchService;
    private readonly IBlocklistService blocklistService;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, TransmissionDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, long> torrentIdToTransmissionId = new();
    private readonly HashSet<int> torrentsHaltedByKillSwitch = new();

    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly SemaphoreSlim vpnKillSwitchTransitionGate = new(1, 1);
    private string transmissionSessionId = string.Empty;
    private Process daemonProcess;
    private CancellationTokenSource syncCts;
    private Task syncLoopTask;

    private bool isRunning;
    private bool disposed;
    private bool isHaltedByKillSwitch;

    public string ProtocolName => "BitTorrent";

    public string EngineId => "Transmission";

    public string DisplayName => "Transmission Daemon (Sidecar)";

    public string Version => "4.0.5 (transmission-daemon)";

    public string ActiveVersion => this.Version;

    public IReadOnlyList<string> SupportedVersions { get; } = new[] { "4.0.5" };

    public string Description => "Isolated, lightweight Transmission daemon running on a local loopback socket. Maximum process isolation and low memory footprint.";

    public bool IsAvailable => CheckDaemonAvailability() || IsRpcEndpointConfigured();

    public TorrentEngineCapabilities GetCapabilitiesForVersion(string version) => this.Capabilities;

    public Task<bool> SwitchVersionAsync(string targetVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetVersion))
        {
            return Task.FromResult(false);
        }

        var trimmed = targetVersion.Trim();
        return Task.FromResult(this.SupportedVersions.Any(v => string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase)));
    }

    public Task RecheckNetworkInterfacesAsync(bool forceRebind, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ClearDhtCacheAsync(bool bootstrapNodes, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public bool IsHaltedByKillSwitch => this.isHaltedByKillSwitch;

    public TorrentEngineCapabilities Capabilities { get; } = new()
    {
        SupportsUtp = true,
        SupportsDht = true,
        SupportsPex = true,
        SupportsLpd = true,
        SupportsV2Torrents = true,
        SupportsSequentialDownload = true,
        SupportsFastResume = true,
        SupportsCustomPiecePickers = false,
        SupportsDynamicRateLimits = true,
        SupportsSparseAllocation = true,
        SupportsMemoryMappedIo = false,
        SupportsEncryptionToggle = true,
    };

    public EmbeddedTransmissionEngine(
        IConfigService configService,
        IStoragePathService storagePathService,
        ICategoryService categoryService,
        IDiskProvider diskProvider,
        IEventAggregator eventAggregator,
        INetworkBindingService networkBindingService = null,
        IVpnKillSwitchService vpnKillSwitchService = null,
        IBlocklistService blocklistService = null,
        HttpClient httpClient = null)
    {
        this.configService = configService;
        this.storagePathService = storagePathService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.networkBindingService = networkBindingService;
        this.vpnKillSwitchService = vpnKillSwitchService;
        this.blocklistService = blocklistService;
        this.logger = LogManager.GetCurrentClassLogger();

        if (httpClient != null)
        {
            this.httpClient = httpClient;
            this.ownsHttpClient = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectTimeout = TimeSpan.FromSeconds(5),
            };

            this.httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            this.ownsHttpClient = true;
        }
    }

    public async Task<EngineHealthCheckResult> ProbeHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        var rpcUrl = GetRpcUrl();
        var checks = new List<string>();
        var warnings = new List<string>();

        try
        {
            var session = await this.SendRpcRequestAsync("session-get", new Dictionary<string, object>());
            sw.Stop();

            if (session.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement args)
            {
                var ver = args.TryGetProperty("version", out var v) ? v.GetString() : "Unknown";
                var rpcVer = args.TryGetProperty("rpc-version", out var rv) ? rv.GetInt32().ToString() : "Unknown";
                var downloadDir = args.TryGetProperty("download-dir", out var dd) ? dd.GetString() : "Default";

                checks.Add($"Transmission RPC reachable at {rpcUrl} (Latency: {sw.ElapsedMilliseconds} ms)");
                checks.Add($"Daemon Version: {ver} (RPC Spec: v{rpcVer})");
                checks.Add($"Default Download Directory: {downloadDir}");

                return new EngineHealthCheckResult
                {
                    IsHealthy = true,
                    StatusMessage = $"Transmission daemon is healthy and responding (v{ver}, {sw.ElapsedMilliseconds}ms).",
                    DependencyChecks = checks,
                    Warnings = warnings,
                };
            }

            return new EngineHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"Transmission RPC responding at {rpcUrl} ({sw.ElapsedMilliseconds}ms).",
                DependencyChecks = checks,
                Warnings = warnings,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            var daemonBinary = GetDaemonBinaryPath();
            if (!string.IsNullOrWhiteSpace(daemonBinary))
            {
                checks.Add($"Transmission binary located: {daemonBinary}");
                warnings.Add($"Daemon is not currently active on {rpcUrl}. It will be started automatically when the engine is activated.");

                return new EngineHealthCheckResult
                {
                    IsHealthy = true,
                    StatusMessage = $"Transmission daemon executable found ({Path.GetFileName(daemonBinary)}). Ready for auto-start.",
                    DependencyChecks = checks,
                    Warnings = warnings,
                };
            }

            checks.Add($"Transmission RPC connection to {rpcUrl} failed: {ex.Message}");
            warnings.Add("Neither a running Transmission daemon RPC endpoint nor a local transmission-daemon binary was detected.");

            return new EngineHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Transmission daemon is unavailable at {rpcUrl}.",
                DependencyChecks = checks,
                Warnings = warnings,
            };
        }
    }

    public Task<EngineHealthCheckResult> ProbeVersionHealthAsync(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return this.ProbeHealthAsync();
        }

        var trimmed = version.Trim();
        if (!this.SupportedVersions.Any(v => string.Equals(v, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return Task.FromResult(new EngineHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"Transmission version '{version}' is not supported.",
                Warnings = new List<string> { $"Supported versions: {string.Join(", ", this.SupportedVersions)}" },
            });
        }

        return this.ProbeHealthAsync();
    }

    public async Task StartAsync()
    {
        if (this.isRunning)
        {
            return;
        }

        this.logger.Info("Starting Transmission engine backend...");

        // Ensure daemon process is running if binary is available
        await this.EnsureDaemonRunningAsync();

        // Apply session settings (interface binding, proxy, blocklist) via RPC
        await this.ConfigureSessionSettingsAsync();

        this.isRunning = true;
        this.syncCts = new CancellationTokenSource();
        this.syncLoopTask = Task.Run(() => this.PollDaemonStateLoopAsync(this.syncCts.Token));

        this.logger.Info("Transmission engine started successfully.");
    }

    public async Task StopAsync()
    {
        if (!this.isRunning)
        {
            return;
        }

        this.logger.Info("Stopping Transmission engine backend...");
        this.isRunning = false;

        if (this.syncCts != null)
        {
            this.syncCts.Cancel();
            try
            {
                if (this.syncLoopTask != null)
                {
                    await Task.WhenAny(this.syncLoopTask, Task.Delay(2000, CancellationToken.None));
                }
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Exception while awaiting sync loop task during engine shutdown");
            }

            this.syncCts.Dispose();
            this.syncCts = null;
        }

        if (this.daemonProcess != null && !this.daemonProcess.HasExited)
        {
            try
            {
                this.daemonProcess.Kill(entireProcessTree: true);
                this.daemonProcess.Dispose();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error terminating Transmission daemon child process.");
            }
            finally
            {
                this.daemonProcess = null;
            }
        }

        this.tasks.Clear();
        this.infoHashToId.Clear();
        this.torrentIdToTransmissionId.Clear();
    }

    public async Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        if (!this.isRunning)
        {
            await this.StartAsync();
        }

        var savePath = this.ResolveSavePath(torrent);
        var addArgs = new Dictionary<string, object>
        {
            ["download-dir"] = savePath,
            ["paused"] = false,
        };

        if (torrentFileBytes != null && torrentFileBytes.Length > 0)
        {
            addArgs["metainfo"] = Convert.ToBase64String(torrentFileBytes);
        }
        else if (!string.IsNullOrWhiteSpace(magnetUri))
        {
            addArgs["filename"] = magnetUri;
        }
        else
        {
            var trackers = !string.IsNullOrWhiteSpace(torrent.TrackerUrl) ? new[] { torrent.TrackerUrl } : null;
            addArgs["filename"] = MagnetLinkParser.BuildMagnetUri(torrent.InfoHash, trackers: trackers);
        }

        var response = await this.SendRpcRequestAsync("torrent-add", addArgs);
        if (!TryGetTransmissionTorrentIdFromAddResponse(response, out var transmissionId))
        {
            throw new InvalidOperationException(
                $"Transmission torrent-add for '{torrent.Name}' did not return a daemon torrent id.");
        }

        var task = new TransmissionDownloadTask(this, torrent.Id, torrent.InfoHash, torrent.Name, torrent.TotalSize, torrent.Category);
        this.tasks[torrent.Id] = task;
        this.infoHashToId[torrent.InfoHash] = torrent.Id;
        if (!string.IsNullOrWhiteSpace(torrent.V2InfoHash))
        {
            this.infoHashToId[torrent.V2InfoHash] = torrent.Id;
        }

        this.torrentIdToTransmissionId[torrent.Id] = transmissionId;
        task.IsSuperSeeding = torrent.InitialSeeding;

        this.logger.Info("Transmission: Ingested torrent {0} ({1}) to {2}", torrent.Name, torrent.InfoHash, savePath);

        return task;
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        if (this.tasks.TryRemove(torrentId, out var task))
        {
            this.infoHashToId.TryRemove(task.InfoHash, out _);
            foreach (var kvp in this.infoHashToId)
            {
                if (kvp.Value == torrentId)
                {
                    this.infoHashToId.TryRemove(kvp.Key, out _);
                }
            }

            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-remove", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                        ["delete-local-data"] = deleteFiles,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching torrent-remove to Transmission for {0}", task.InfoHash);
            }
            finally
            {
                this.torrentIdToTransmissionId.TryRemove(torrentId, out _);
            }

            this.logger.Info("Transmission: Removed torrent {0} (deleteFiles: {1})", task.InfoHash, deleteFiles);
        }
    }

    public async Task PauseTorrentAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = TorrentStatus.Paused;
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-stop", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching torrent-stop to Transmission for id {0}", torrentId);
            }

            this.logger.Info("Transmission: Paused torrent id {0}", torrentId);
        }
    }

    public async Task ResumeTorrentAsync(int torrentId)
    {
        if (this.isHaltedByKillSwitch)
        {
            this.logger.Warn("Cannot resume torrent id {0}: VPN Kill Switch is active (fail-closed).", torrentId);
            return;
        }

        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = task.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-start", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching torrent-start to Transmission for id {0}", torrentId);
            }

            this.logger.Info("Transmission: Resumed torrent id {0}", torrentId);
        }
    }

    public async Task PauseAllTorrentsAsync()
    {
        foreach (var task in this.tasks.Values)
        {
            if (task.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Queued or TorrentStatus.Completed or TorrentStatus.QueuedForChecking)
            {
                continue;
            }

            await this.PauseTorrentAsync(task.TorrentId);
        }
    }

    public Task PauseAllAsync() => this.PauseAllTorrentsAsync();

    public Task PauseForSchedulerAsync() => this.PauseAllTorrentsAsync();

    public async Task ResumeAllTorrentsAsync()
    {
        if (this.isHaltedByKillSwitch)
        {
            this.logger.Warn("Cannot resume all torrents: VPN Kill Switch is active (fail-closed).");
            return;
        }

        foreach (var task in this.tasks.Values)
        {
            if (task.Status == TorrentStatus.Paused)
            {
                await this.ResumeTorrentAsync(task.TorrentId);
            }
        }
    }

    public Task ResumeAllAsync() => this.ResumeAllTorrentsAsync();

    public Task ResumeFromSchedulerAsync() => this.ResumeAllTorrentsAsync();

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        this.logger.Error("VPN Kill Switch drop detected for interface '{0}'. Halting Transmission engine transfers.", message.InterfaceName);
        this.isHaltedByKillSwitch = true;

        TorrentEngineNetworkHelper.HaltActiveTorrents(
            this.tasks.Values,
            this.torrentsHaltedByKillSwitch,
            t => t.TorrentId,
            t => t.Status,
            (t, s) => t.Status = s);

        _ = Task.Run(async () =>
        {
            await this.vpnKillSwitchTransitionGate.WaitAsync(this.syncCts?.Token ?? CancellationToken.None);
            try
            {
                await this.RunVpnKillSwitchHaltRpcAsync(this.syncCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to stop torrents or update session settings during killswitch activation");
            }
            finally
            {
                this.vpnKillSwitchTransitionGate.Release();
            }
        }, this.syncCts?.Token ?? CancellationToken.None);
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        this.logger.Info("VPN interface '{0}' restored. Resuming Transmission engine transfers.", message.InterfaceName);
        this.isHaltedByKillSwitch = false;

        var resumeIds = TorrentEngineNetworkHelper.PrepareHaltedTorrentsForRestore(
            this.tasks,
            this.torrentsHaltedByKillSwitch,
            (t, s) => t.Status = s,
            t => t.Progress);

        _ = Task.Run(async () =>
        {
            await this.vpnKillSwitchTransitionGate.WaitAsync(this.syncCts?.Token ?? CancellationToken.None);
            try
            {
                await this.RunVpnInterfaceRestoreRpcAsync(resumeIds, this.syncCts?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Failed to update session settings during killswitch deactivation");
            }
            finally
            {
                this.vpnKillSwitchTransitionGate.Release();
            }
        }, this.syncCts?.Token ?? CancellationToken.None);
    }

    public void Handle(NetworkBindingProviderSwitchedEvent message)
    {
        this.logger.Info("Transmission: Network binding provider switched ({0} -> {1}). Recycling peer sessions.", message.PreviousProvider, message.NewProvider);
        _ = Task.Run(async () =>
        {
            try
            {
                await this.ConfigureSessionSettingsAsync();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error updating Transmission session settings on network binding switch.");
            }
        }, this.syncCts?.Token ?? CancellationToken.None);
    }

    public void Handle(ConfigSavedEvent message)
    {
        if (!this.isRunning)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await this.ConfigureSessionSettingsAsync();
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error updating Transmission session settings on config change.");
            }
        }, this.syncCts?.Token ?? CancellationToken.None);
    }

    public async Task ForceRecheckAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = TorrentStatus.Checking;
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-verify", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching torrent-verify for id {0}", torrentId);
            }

            this.logger.Info("Transmission: Triggered verify for torrent id {0}", torrentId);
        }
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-reannounce", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching torrent-reannounce for id {0}", torrentId);
            }
        }
    }

    public async Task<IReadOnlyList<string>> AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        var trackerList = trackers?.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();
        if (trackerList.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-set", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                        ["trackerAdd"] = trackerList.ToArray(),
                    });
                    return trackerList;
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error adding trackers in Transmission for {0}", torrentId);
            }
        }

        return Array.Empty<string>();
    }

    public async Task RemoveTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    var trackerList = trackers.ToList();
                    var trackerIdsToRemove = new List<int>();
                    var nonIntTrackers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var tr in trackerList)
                    {
                        if (int.TryParse(tr, out var trId))
                        {
                            trackerIdsToRemove.Add(trId);
                        }
                        else
                        {
                            nonIntTrackers.Add(tr);
                        }
                    }

                    if (nonIntTrackers.Count > 0)
                    {
                        var getResp = await this.SendRpcRequestAsync("torrent-get", new Dictionary<string, object>
                        {
                            ["ids"] = rpcIds,
                            ["fields"] = TrackerFields,
                        });

                        if (getResp.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement args && args.TryGetProperty("torrents", out var torrentsArray))
                        {
                            foreach (var item in torrentsArray.EnumerateArray())
                            {
                                if (item.TryGetProperty("trackers", out var tArr))
                                {
                                    foreach (var trackerObj in tArr.EnumerateArray())
                                    {
                                        var announce = trackerObj.TryGetProperty("announce", out var a) ? a.GetString() : null;
                                        var tId = trackerObj.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : -1;
                                        if (tId >= 0 && announce != null && nonIntTrackers.Any(requested => TrackerAnnouncesMatch(requested, announce)))
                                        {
                                            trackerIdsToRemove.Add(tId);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (trackerIdsToRemove.Count > 0)
                    {
                        await this.SendRpcRequestAsync("torrent-set", new Dictionary<string, object>
                        {
                            ["ids"] = rpcIds,
                            ["trackerRemove"] = trackerIdsToRemove.Distinct().ToArray(),
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error removing trackers in Transmission for {0}", torrentId);
            }
        }
    }

    public async Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    var fileIndex = -1;
                    if (!int.TryParse(filePath, out fileIndex))
                    {
                        var getResp = await this.SendRpcRequestAsync("torrent-get", new Dictionary<string, object>
                        {
                            ["ids"] = rpcIds,
                            ["fields"] = FileFields,
                        });

                        if (getResp.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement args && args.TryGetProperty("torrents", out var torrentsArray))
                        {
                            foreach (var item in torrentsArray.EnumerateArray())
                            {
                                if (item.TryGetProperty("files", out var fArr))
                                {
                                    var idx = 0;
                                    foreach (var f in fArr.EnumerateArray())
                                    {
                                        var name = f.TryGetProperty("name", out var n) ? n.GetString() : null;
                                        if (TransmissionTorrentFilePathsMatch(filePath, name))
                                        {
                                            fileIndex = idx;
                                            break;
                                        }

                                        idx++;
                                    }
                                }

                                if (fileIndex >= 0)
                                {
                                    break;
                                }
                            }
                        }
                    }

                    if (fileIndex >= 0)
                    {
                        var setArgs = new Dictionary<string, object>
                        {
                            ["ids"] = rpcIds,
                        };

                        if (priority == 0)
                        {
                            setArgs["files-unwanted"] = new[] { fileIndex };
                        }
                        else
                        {
                            setArgs["files-wanted"] = new[] { fileIndex };
                            if (priority >= 4)
                            {
                                setArgs["priority-high"] = new[] { fileIndex };
                            }
                            else if (priority <= 2)
                            {
                                setArgs["priority-low"] = new[] { fileIndex };
                            }
                            else
                            {
                                setArgs["priority-normal"] = new[] { fileIndex };
                            }
                        }

                        await this.SendRpcRequestAsync("torrent-set", setArgs);
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error setting file priority in Transmission for {0} (file: {1}, priority: {2})", torrentId, filePath, priority);
            }
        }
    }

    public async Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        try
        {
            var args = new Dictionary<string, object>();
            if (maxDownloadKbps > 0)
            {
                args["speed-limit-down"] = maxDownloadKbps;
                args["speed-limit-down-enabled"] = true;
            }
            else
            {
                args["speed-limit-down-enabled"] = false;
            }

            if (maxUploadKbps > 0)
            {
                args["speed-limit-up"] = maxUploadKbps;
                args["speed-limit-up-enabled"] = true;
            }
            else
            {
                args["speed-limit-up-enabled"] = false;
            }

            await this.SendRpcRequestAsync("session-set", args);
            this.logger.Info("Transmission: Set global rate limits: DL {0} KB/s, UL {1} KB/s", maxDownloadKbps, maxUploadKbps);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error setting rate limits in Transmission");
        }
    }

    public async Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    var args = new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                    };

                    if (maxDownloadKbps > 0)
                    {
                        args["downloadLimit"] = maxDownloadKbps;
                        args["downloadLimited"] = true;
                    }
                    else
                    {
                        args["downloadLimited"] = false;
                    }

                    if (maxUploadKbps > 0)
                    {
                        args["uploadLimit"] = maxUploadKbps;
                        args["uploadLimited"] = true;
                    }
                    else
                    {
                        args["uploadLimited"] = false;
                    }

                    await this.SendRpcRequestAsync("torrent-set", args);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error setting torrent rate limits for id {0}", torrentId);
            }
        }
    }

    public async Task SetTorrentPrivateStatusAsync(int torrentId, bool isPrivate)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-set", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                        ["isPrivate"] = isPrivate,
                    });
                    this.logger.Info("Transmission: Updated BEP 27 settings for {0} (IsPrivate: {1})", task.InfoHash, isPrivate);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error setting private status in Transmission for {0}", torrentId);
            }
        }
    }

    public async Task SetSuperSeedingAsync(int torrentId, bool enabled)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-set", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                        ["seedMode"] = enabled,
                    });
                    task.IsSuperSeeding = enabled;
                    this.logger.Info("Transmission: Set super seeding for torrent id {0}: {1}", torrentId, enabled);
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error setting super seeding in Transmission for {0}", torrentId);
            }
        }
    }

    public async Task MoveTorrentFilesAsync(int torrentId, string newSavePath, bool moveFiles = true)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
                if (rpcIds.Count > 0)
                {
                    await this.SendRpcRequestAsync("torrent-set-location", new Dictionary<string, object>
                    {
                        ["ids"] = rpcIds,
                        ["location"] = newSavePath,
                        ["move"] = moveFiles,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Error(ex, "Error moving files for torrent {0}", torrentId);
                throw;
            }
        }
    }

    public async Task<bool> RenameFileAsync(int torrentId, string oldRelativePath, string newRelativePath)
    {
        return await this.RenameTorrentPathAsync(torrentId, oldRelativePath, newRelativePath);
    }

    public async Task<bool> RenameFolderAsync(int torrentId, string oldRelativeFolder, string newRelativeFolder)
    {
        return await this.RenameTorrentPathAsync(torrentId, oldRelativeFolder, newRelativeFolder);
    }

    private async Task<bool> RenameTorrentPathAsync(int torrentId, string oldRelativePath, string newRelativePath)
    {
        if (string.IsNullOrWhiteSpace(oldRelativePath) || string.IsNullOrWhiteSpace(newRelativePath))
        {
            return false;
        }

        if (!this.tasks.TryGetValue(torrentId, out var task))
        {
            this.logger.Warn("Cannot rename path: torrent {0} not found in Transmission engine", torrentId);
            return false;
        }

        var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
        if (rpcIds.Count == 0)
        {
            return false;
        }

        try
        {
            var response = await this.SendRpcRequestAsync("torrent-rename-path", new Dictionary<string, object>
            {
                ["ids"] = rpcIds,
                ["path"] = oldRelativePath.Replace('\\', '/').TrimStart('/'),
                ["name"] = newRelativePath.Replace('\\', '/').TrimStart('/'),
            });

            return IsTransmissionRpcSuccess(response);
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to rename path in torrent {0} from '{1}' to '{2}'", torrentId, oldRelativePath, newRelativePath);
            return false;
        }
    }

    private static bool IsTransmissionRpcSuccess(Dictionary<string, object> response)
    {
        if (response == null || !response.TryGetValue("result", out var resultObj) || resultObj == null)
        {
            return false;
        }

        if (resultObj is JsonElement resultElement)
        {
            return resultElement.ValueKind == JsonValueKind.String &&
                   string.Equals(resultElement.GetString(), "success", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(resultObj.ToString(), "success", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureTransmissionRpcSuccess(Dictionary<string, object> response, string method)
    {
        if (IsTransmissionRpcSuccess(response))
        {
            return;
        }

        var resultText = response != null && response.TryGetValue("result", out var resultObj) && resultObj != null
            ? resultObj is JsonElement resultElement && resultElement.ValueKind == JsonValueKind.String
                ? resultElement.GetString()
                : resultObj.ToString()
            : "unknown";

        throw new InvalidOperationException($"Transmission RPC '{method}' failed with result '{resultText}'.");
    }

    private static bool TryGetTransmissionTorrentIdFromAddResponse(Dictionary<string, object> response, out long transmissionId)
    {
        transmissionId = 0;
        if (response == null || !response.TryGetValue("arguments", out var argsObj) || argsObj is not JsonElement args)
        {
            return false;
        }

        if (!args.TryGetProperty("torrent-added", out var added) && !args.TryGetProperty("torrent-duplicate", out added))
        {
            return false;
        }

        if (!added.TryGetProperty("id", out var idProp) || !idProp.TryGetInt64(out transmissionId))
        {
            return false;
        }

        return transmissionId > 0;
    }

    public IDownloadTask GetTask(int torrentId)
    {
        this.tasks.TryGetValue(torrentId, out var task);
        return task;
    }

    public async Task<bool> DisconnectPeerAsync(int torrentId, string ip)
    {
        if (!this.tasks.TryGetValue(torrentId, out var task))
        {
            return false;
        }

        var cleanIp = TorrentEngineNetworkHelper.NormalizePeerIp(ip);
        if (string.IsNullOrWhiteSpace(cleanIp))
        {
            return false;
        }

        var rpcIds = this.GetRpcIdsForTorrent(torrentId, task.InfoHash);
        if (rpcIds.Count == 0)
        {
            return false;
        }

        var hasPeer = task.GetPeers().Any(p => TorrentEngineNetworkHelper.PeerAddressesMatch(cleanIp, p.Ip));
        if (!hasPeer)
        {
            hasPeer = await this.TorrentHasPeerViaRpcAsync(rpcIds, cleanIp);
            if (!hasPeer)
            {
                return false;
            }
        }

        try
        {
            await this.SendRpcRequestAsync("torrent-stop", new Dictionary<string, object> { ["ids"] = rpcIds });
            await this.SendRpcRequestAsync("torrent-start", new Dictionary<string, object> { ["ids"] = rpcIds });

            var remainingPeers = task.GetPeers()
                .Where(p => !TorrentEngineNetworkHelper.PeerAddressesMatch(cleanIp, p.Ip))
                .ToList();
            task.SetPeers(remainingPeers);
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Transmission: failed to disconnect peer {0} on torrent {1}", ip, torrentId);
            return false;
        }
    }

    public IEnumerable<IDownloadTask> GetAllTasks()
    {
        return this.tasks.Values;
    }

    public TorrentEngineMetrics GetEngineMetrics() => new()
    {
        EngineId = this.EngineId,
        DisplayName = this.DisplayName,
        Version = this.Version,
        IsRunning = this.isRunning,
        ActiveTorrents = this.tasks.Count,
    };

    public TorrentResourceMetrics GetTorrentResourceMetrics(int torrentId) =>
        this.tasks.TryGetValue(torrentId, out var task) ? task.GetResourceMetrics() : null;

    public IReadOnlyList<TorrentResourceMetrics> GetAllTorrentResourceMetrics() =>
        this.tasks.Values.Select(t => t.GetResourceMetrics()).ToList();

    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            this.StopAsync().GetAwaiter().GetResult();
            this.vpnKillSwitchTransitionGate.Dispose();
            if (this.ownsHttpClient)
            {
                this.httpClient.Dispose();
            }
        }
    }

    private async Task PollDaemonStateLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var fields = new[]
                {
                    "id", "hashString", "name", "status", "percentDone",
                    "rateDownload", "rateUpload", "peersConnected", "peersSendingToUs",
                    "peersGettingFromUs", "totalSize", "downloadedEver", "uploadedEver",
                    "pieceCount", "pieces", "peers", "availability", "seedMode",
                };

                var response = await this.SendRpcRequestAsync("torrent-get", new Dictionary<string, object>
                {
                    ["fields"] = fields,
                });

                if (response.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement args && args.TryGetProperty("torrents", out var torrentsArray))
                {
                    foreach (var item in torrentsArray.EnumerateArray())
                    {
                        var hash = item.TryGetProperty("hashString", out var h) ? h.GetString() : null;
                        if (string.IsNullOrWhiteSpace(hash) || !this.infoHashToId.TryGetValue(hash, out var torrentId))
                        {
                            continue;
                        }

                        if (item.TryGetProperty("id", out var transId))
                        {
                            this.torrentIdToTransmissionId[torrentId] = transId.GetInt64();
                        }

                        if (this.tasks.TryGetValue(torrentId, out var task))
                        {
                            task.Progress = item.TryGetProperty("percentDone", out var pd) ? pd.GetDouble() : task.Progress;
                            task.DownloadSpeed = item.TryGetProperty("rateDownload", out var rd) ? rd.GetInt64() : 0;
                            task.UploadSpeed = item.TryGetProperty("rateUpload", out var ru) ? ru.GetInt64() : 0;
                            task.DownloadedBytes = item.TryGetProperty("downloadedEver", out var de) ? de.GetInt64() : task.DownloadedBytes;
                            task.UploadedBytes = item.TryGetProperty("uploadedEver", out var ue) ? ue.GetInt64() : task.UploadedBytes;

                            var statusCode = item.TryGetProperty("status", out var st) ? st.GetInt32() : 0;
                            task.Status = statusCode switch
                            {
                                0 => TorrentStatus.Paused,
                                1 => TorrentStatus.Checking,
                                2 => TorrentStatus.Checking,
                                3 => TorrentStatus.Queued,
                                4 => TorrentStatus.Downloading,
                                5 => TorrentStatus.Queued,
                                6 => TorrentStatus.Seeding,
                                _ => TorrentStatus.Downloading,
                            };

                            if (item.TryGetProperty("seedMode", out var seedMode) &&
                                (seedMode.ValueKind == JsonValueKind.True || seedMode.ValueKind == JsonValueKind.False))
                            {
                                task.SyncSuperSeedingFromEngine(seedMode.GetBoolean());
                            }

                            if (item.TryGetProperty("availability", out var pieceAvailability) && pieceAvailability.ValueKind == JsonValueKind.Array)
                            {
                                var availability = new int[pieceAvailability.GetArrayLength()];
                                var index = 0;
                                foreach (var piece in pieceAvailability.EnumerateArray())
                                {
                                    availability[index++] = (int)Math.Round(piece.GetDouble());
                                }

                                task.PieceAvailability = availability;
                            }

                            if (item.TryGetProperty("peers", out var peersArray) && peersArray.ValueKind == JsonValueKind.Array)
                            {
                                var peerList = new List<PeerInfo>();
                                foreach (var p in peersArray.EnumerateArray())
                                {
                                    var ip = p.TryGetProperty("address", out var addr) ? addr.GetString() : "unknown";
                                    var port = p.TryGetProperty("port", out var prt) ? prt.GetInt32() : 0;
                                    var client = p.TryGetProperty("clientName", out var cn) ? cn.GetString() : string.Empty;
                                    var flags = p.TryGetProperty("flagStr", out var fs) ? fs.GetString() : string.Empty;
                                    var progress = p.TryGetProperty("progress", out var prg) ? prg.GetDouble() : 0.0;
                                    var rateToClient = p.TryGetProperty("rateToClient", out var rtc) ? rtc.GetInt64() : 0;
                                    var rateToPeer = p.TryGetProperty("rateToPeer", out var rtp) ? rtp.GetInt64() : 0;
                                    var isEncrypted = p.TryGetProperty("isEncrypted", out var enc) && enc.GetBoolean();
                                    var isUtp = p.TryGetProperty("isUTP", out var utp) && utp.GetBoolean();
                                    var isIncoming = p.TryGetProperty("isIncoming", out var inc) && inc.GetBoolean();
                                    var peerIsChoked = p.TryGetProperty("peerIsChoked", out var pic) && pic.GetBoolean();
                                    var peerIsInterested = p.TryGetProperty("peerIsInterested", out var pii) && pii.GetBoolean();
                                    var clientIsChoked = p.TryGetProperty("clientIsChoked", out var cic) && cic.GetBoolean();
                                    var clientIsInterested = p.TryGetProperty("clientIsInterested", out var cii) && cii.GetBoolean();
                                    var downloaded = p.TryGetProperty("bytesToClient", out var btc) ? btc.GetInt64() : 0;
                                    var uploaded = p.TryGetProperty("bytesToPeer", out var btp) ? btp.GetInt64() : 0;

                                    peerList.Add(new PeerInfo
                                    {
                                        Ip = ip ?? "unknown",
                                        Port = port,
                                        Client = client ?? string.Empty,
                                        Flags = flags ?? string.Empty,
                                        Progress = progress,
                                        DownloadSpeed = rateToClient,
                                        UploadSpeed = rateToPeer,
                                        Downloaded = downloaded,
                                        Uploaded = uploaded,
                                        IsEncrypted = isEncrypted,
                                        IsChoked = peerIsChoked,
                                        IsInterested = peerIsInterested,
                                        ClientIsChoked = clientIsChoked,
                                        ClientIsInterested = clientIsInterested,
                                        IsIncoming = isIncoming,
                                        IsUtp = isUtp,
                                    });
                                }

                                task.SetPeers(peerList);
                                var (connectedSeeders, connectedLeechers) = ResolveConnectedPeerCountsFromTransmissionPoll(item, peerList);
                                task.ConnectedSeeders = connectedSeeders;
                                task.ConnectedLeechers = connectedLeechers;
                            }
                            else
                            {
                                var (connectedSeeders, connectedLeechers) = ResolveConnectedPeerCountsFromTransmissionPoll(item, null);
                                task.ConnectedSeeders = connectedSeeders;
                                task.ConnectedLeechers = connectedLeechers;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Daemon poll sync error, will retry on next tick");
            }

            try
            {
                await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private List<object> CollectRpcIdsForHaltedTorrents()
    {
        var rpcIds = new List<object>();
        lock (this.torrentsHaltedByKillSwitch)
        {
            foreach (var torrentId in this.torrentsHaltedByKillSwitch)
            {
                if (this.tasks.TryGetValue(torrentId, out var task))
                {
                    rpcIds.AddRange(this.GetRpcIdsForTorrent(torrentId, task.InfoHash));
                }
            }
        }

        return rpcIds;
    }

    private async Task RunVpnKillSwitchHaltRpcAsync(CancellationToken cancellationToken)
    {
        var rpcIds = this.CollectRpcIdsForHaltedTorrents();
        if (rpcIds.Count > 0)
        {
            await this.SendRpcRequestAsync("torrent-stop", new Dictionary<string, object>
            {
                ["ids"] = rpcIds,
            });
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (this.isHaltedByKillSwitch)
        {
            await this.ConfigureSessionSettingsAsync();
        }
    }

    private async Task RunVpnInterfaceRestoreRpcAsync(IReadOnlyList<int> resumeIds, CancellationToken cancellationToken)
    {
        await this.ConfigureSessionSettingsAsync();

        cancellationToken.ThrowIfCancellationRequested();

        if (this.isHaltedByKillSwitch)
        {
            return;
        }

        foreach (var torrentId in resumeIds)
        {
            await this.ResumeTorrentAsync(torrentId);
        }
    }

    private async Task<Dictionary<string, object>> SendRpcRequestAsync(string method, Dictionary<string, object> arguments)
    {
        var rpcUrl = GetRpcUrl();
        var payload = new Dictionary<string, object>
        {
            ["method"] = method,
            ["arguments"] = arguments,
            ["tag"] = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 1000000),
        };

        var json = payload.ToJson();
        using var request = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(this.transmissionSessionId))
        {
            request.Headers.TryAddWithoutValidation("X-Transmission-Session-Id", this.transmissionSessionId);
        }

        var response = await this.httpClient.SendAsync(request);

        // Transmission CSRF token handshake (409 Conflict)
        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            if (response.Headers.TryGetValues("X-Transmission-Session-Id", out var values))
            {
                this.transmissionSessionId = values.FirstOrDefault() ?? string.Empty;

                using var retryRequest = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                retryRequest.Headers.TryAddWithoutValidation("X-Transmission-Session-Id", this.transmissionSessionId);

                var retryResponse = await this.httpClient.SendAsync(retryRequest);
                retryResponse.EnsureSuccessStatusCode();
                var retryContent = await retryResponse.Content.ReadAsStringAsync();
                var retryDict = retryContent.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();
                EnsureTransmissionRpcSuccess(retryDict, method);
                return retryDict;
            }
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var responseDict = content.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();
        EnsureTransmissionRpcSuccess(responseDict, method);
        return responseDict;
    }

    private List<object> GetRpcIdsForTorrent(int torrentId, string infoHash)
    {
        var list = new List<object>();
        if (this.torrentIdToTransmissionId.TryGetValue(torrentId, out var transId))
        {
            list.Add(transId);
        }
        else if (!string.IsNullOrWhiteSpace(infoHash))
        {
            list.Add(infoHash);
        }

        return list;
    }

    private async Task<bool> TorrentHasPeerViaRpcAsync(List<object> rpcIds, string cleanIp)
    {
        try
        {
            var getResp = await this.SendRpcRequestAsync("torrent-get", new Dictionary<string, object>
            {
                ["ids"] = rpcIds,
                ["fields"] = PeerFields,
            });

            if (!getResp.TryGetValue("arguments", out var argsObj) || argsObj is not JsonElement args)
            {
                return false;
            }

            if (!args.TryGetProperty("torrents", out var torrentsArray) || torrentsArray.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in torrentsArray.EnumerateArray())
            {
                if (!item.TryGetProperty("peers", out var peersArray) || peersArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var peer in peersArray.EnumerateArray())
                {
                    if (!peer.TryGetProperty("address", out var addressProp))
                    {
                        continue;
                    }

                    var address = addressProp.GetString();
                    if (TorrentEngineNetworkHelper.PeerAddressesMatch(cleanIp, address))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            this.logger.Debug(ex, "Transmission: failed to query peers while disconnecting {0}", cleanIp);
        }

        return false;
    }

    private static bool TrackerAnnouncesMatch(string requested, string announce)
    {
        if (string.IsNullOrWhiteSpace(requested) || string.IsNullOrWhiteSpace(announce))
        {
            return false;
        }

        if (string.Equals(requested, announce, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            NormalizeTrackerAnnounce(requested),
            NormalizeTrackerAnnounce(announce),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTrackerAnnounce(string announce)
    {
        return announce.TrimEnd('/');
    }

    private static string GetRpcUrl()
    {
        var envUrl = Environment.GetEnvironmentVariable("TRANSMISSION_RPC_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl;
        }

        return "http://127.0.0.1:9091/transmission/rpc";
    }

    private static bool IsRpcEndpointConfigured()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TRANSMISSION_RPC_URL"));
    }

    private static bool CheckDaemonAvailability()
    {
        try
        {
            var binary = GetDaemonBinaryPath();
            return !string.IsNullOrWhiteSpace(binary);
        }
        catch
        {
            return false;
        }
    }

    private static string GetDaemonBinaryPath()
    {
        var customPath = Environment.GetEnvironmentVariable("TRANSMISSION_DAEMON_PATH");
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir, "transmission-daemon");
            if (File.Exists(full) || File.Exists(full + ".exe"))
            {
                return full;
            }
        }

        return null;
    }

    public async Task<Dictionary<string, object>> ConfigureSessionSettingsAsync()
    {
        var sessionArgs = new Dictionary<string, object>
        {
            ["bind-address-ipv4"] = this.ResolveBoundIpv4Address(),
        };

        var port = this.configService?.ListeningPort > 0 ? this.configService.ListeningPort : 51413;
        sessionArgs["peer-port"] = port;

        var maxDownloadSpeedKbps = this.configService?.MaxDownloadSpeedKbps ?? 0;
        if (maxDownloadSpeedKbps > 0)
        {
            sessionArgs["speed-limit-down"] = maxDownloadSpeedKbps;
            sessionArgs["speed-limit-down-enabled"] = true;
        }
        else
        {
            sessionArgs["speed-limit-down-enabled"] = false;
        }

        var maxUploadSpeedKbps = this.configService?.MaxUploadSpeedKbps ?? 0;
        if (maxUploadSpeedKbps > 0)
        {
            sessionArgs["speed-limit-up"] = maxUploadSpeedKbps;
            sessionArgs["speed-limit-up-enabled"] = true;
        }
        else
        {
            sessionArgs["speed-limit-up-enabled"] = false;
        }

        var isAltSpeed = this.configService?.AlternativeSpeedEnabled ?? false;
        sessionArgs["alt-speed-enabled"] = isAltSpeed;
        sessionArgs["alt-speed-down"] = this.configService?.AltDownloadSpeedKbps < 0
            ? 1
            : (this.configService?.AltDownloadSpeedKbps ?? 2000);
        sessionArgs["alt-speed-up"] = this.configService?.AltUploadSpeedKbps < 0
            ? 1
            : (this.configService?.AltUploadSpeedKbps ?? 500);

        var isAnonymous = this.configService?.AnonymousMode ?? false;
        sessionArgs["dht-enabled"] = (this.configService?.EnableDht ?? true) && !isAnonymous;
        sessionArgs["pex-enabled"] = ProtocolExtensionPolicy.IsUtPexEnabled(this.configService) && !isAnonymous;
        sessionArgs["lpd-enabled"] = (this.configService?.EnableLpd ?? true) && !isAnonymous;
        sessionArgs["encryption"] = MapTransmissionEncryptionPreference(this.configService?.EncryptionMode);

        var proxyType = this.configService?.ProxyType?.ToLowerInvariant();
        var proxyConfigured = !string.IsNullOrWhiteSpace(proxyType) &&
            !string.Equals(proxyType, "none", StringComparison.OrdinalIgnoreCase);

        if (proxyConfigured)
        {
            sessionArgs["proxy-enabled"] = true;
            sessionArgs["proxy-type"] = proxyType;
            sessionArgs["proxy-host"] = this.configService?.ProxyHost ?? string.Empty;
            sessionArgs["proxy-port"] = this.configService.ProxyPort;
            sessionArgs["proxy-auth-enabled"] = this.configService.ProxyAuthEnabled;
            sessionArgs["proxy-auth-username"] = this.configService?.ProxyUsername ?? string.Empty;
            sessionArgs["proxy-auth-password"] = this.configService?.ProxyPassword ?? string.Empty;
        }
        else
        {
            sessionArgs["proxy-enabled"] = false;
        }

        var blocklistEnabled = this.configService?.BlocklistEnabled ?? false;
        sessionArgs["blocklist-enabled"] = blocklistEnabled;
        if (blocklistEnabled)
        {
            sessionArgs["blocklist-url"] = this.configService?.BlocklistUrl ?? string.Empty;
        }

        try
        {
            var response = await this.SendRpcRequestAsync("session-set", sessionArgs);
            this.logger.Info(
                "Transmission: Updated session settings (bind={0}, port={1}, proxy-enabled={2}, blocklist={3}, dht={4}, pex={5}, lpd={6}, encryption={7})",
                sessionArgs["bind-address-ipv4"],
                sessionArgs["peer-port"],
                sessionArgs["proxy-enabled"],
                sessionArgs["blocklist-enabled"],
                sessionArgs["dht-enabled"],
                sessionArgs["pex-enabled"],
                sessionArgs["lpd-enabled"],
                sessionArgs["encryption"]);
            return response;
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error configuring Transmission session settings.");
            return null;
        }
    }

    internal static string MapTransmissionEncryptionPreference(string encryptionMode)
    {
        return encryptionMode?.Trim().ToLowerInvariant() switch
        {
            "forceencrypted" or "forced" or "requireencrypted" or "forcedencryption" or "required" => "required",
            "preferencrypted" or "enabled" or "preferred" => "preferred",
            "allowplaintext" or "disabled" or "plaintext" or "none" => "allowed",
            _ => "preferred",
        };
    }

    internal static bool TransmissionTorrentFilePathsMatch(string requestedPath, string transmissionFileName)
    {
        if (string.IsNullOrWhiteSpace(requestedPath) || string.IsNullOrWhiteSpace(transmissionFileName))
        {
            return false;
        }

        var normalized = requestedPath.Replace('\\', '/').TrimStart('/');
        var name = transmissionFileName.Replace('\\', '/').TrimStart('/');
        return name.Equals(normalized, StringComparison.OrdinalIgnoreCase);
    }

    internal static (int Seeders, int Leechers) ResolveConnectedPeerCountsFromTransmissionPoll(JsonElement torrent, IReadOnlyList<PeerInfo> peers)
    {
        if (peers != null && peers.Count > 0)
        {
            return (peers.Count(p => p.Progress >= 1.0), peers.Count(p => p.Progress < 1.0));
        }

        if (torrent.TryGetProperty("peersConnected", out var peersConnected) && peersConnected.TryGetInt32(out var total) && total > 0)
        {
            return (0, total);
        }

        return (0, 0);
    }

    internal string ResolveBoundIpv4Address()
    {
        return TorrentEngineNetworkHelper.ResolveBoundIpv4Address(
            this.isHaltedByKillSwitch,
            this.vpnKillSwitchService,
            this.configService,
            this.networkBindingService);
    }

    private async Task EnsureDaemonRunningAsync()
    {
        var binary = GetDaemonBinaryPath();

        try
        {
            await this.VerifyRpcSessionAsync();
            return;
        }
        catch (Exception ex)
        {
            if (string.IsNullOrWhiteSpace(binary))
            {
                throw;
            }

            this.logger.Trace(ex, "Transmission daemon probe failed, starting daemon child process");
        }

        try
        {
            var configDir = Path.Combine(Path.GetTempPath(), "leecharr-transmission");
            Directory.CreateDirectory(configDir);

            var bindIp = this.ResolveBoundIpv4Address();
            var arguments = $"--foreground --config-dir \"{configDir}\" --port 9091 --allowed 127.0.0.1,::1";

            if (!string.IsNullOrWhiteSpace(bindIp))
            {
                arguments += $" --bind-address-ipv4 {bindIp}";
            }

            if (this.configService?.BlocklistEnabled == true)
            {
                arguments += " --blocklist";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = binary, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
                Arguments = arguments, /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            this.daemonProcess = Process.Start(startInfo); /* cmd-safe: fixed/admin-configured binary, args via ProcessStartInfo, no shell */ // nosemgrep
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to start transmission-daemon child process automatically.");
        }

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(250, this.syncCts?.Token ?? CancellationToken.None);
            try
            {
                await this.VerifyRpcSessionAsync();
                return;
            }
            catch (Exception ex)
            {
                this.logger.Trace(ex, "Waiting for transmission-daemon RPC after start (attempt {0})", attempt + 1);
            }
        }

        throw new InvalidOperationException("Transmission daemon is not reachable after start attempt.");
    }

    private Task VerifyRpcSessionAsync()
    {
        return this.SendRpcRequestAsync("session-get", new Dictionary<string, object>());
    }

    private string ResolveSavePath(Torrent torrent)
    {
        var defaultCompletedDir = this.storagePathService.GetCompletedDirectory(torrent.Category);
        return this.categoryService.GetSavePathForCategory(torrent.Category, defaultCompletedDir);
    }
}

public class TransmissionDownloadTask : BaseDaemonDownloadTask
{
    private readonly EmbeddedTransmissionEngine engine;

    public TransmissionDownloadTask(
        EmbeddedTransmissionEngine engine,
        int torrentId,
        string infoHash,
        string name,
        long totalSize,
        string category = null)
        : base(torrentId, infoHash, name, totalSize, category)
    {
        this.engine = engine;
    }

    public Task<bool> DisconnectPeerAsync(string ip) => this.engine.DisconnectPeerAsync(this.TorrentId, ip);
}
