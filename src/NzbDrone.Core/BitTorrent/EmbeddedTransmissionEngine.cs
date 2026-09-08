// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.BitTorrent;

public class EmbeddedTransmissionEngine : ITorrentEngine, IDisposable, IHandle<VpnKillSwitchTriggeredEvent>, IHandle<VpnInterfaceRestoredEvent>, IHandle<NetworkBindingProviderSwitchedEvent>
{
    private readonly IConfigService configService;
    private readonly IStoragePathService storagePathService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, TransmissionDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, long> torrentIdToTransmissionId = new();
    private readonly HashSet<int> torrentsHaltedByKillSwitch = new();

    private readonly HttpClient httpClient;
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

    public string Description => "Isolated, lightweight Transmission daemon running on a local loopback socket. Maximum process isolation and low memory footprint.";

    public bool IsAvailable => CheckDaemonAvailability() || IsRpcEndpointConfigured();

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
        IEventAggregator eventAggregator)
    {
        this.configService = configService;
        this.storagePathService = storagePathService;
        this.categoryService = categoryService;
        this.diskProvider = diskProvider;
        this.eventAggregator = eventAggregator;
        this.logger = LogManager.GetCurrentClassLogger();

        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };

        this.httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    public async Task<EngineHealthCheckResult> ProbeHealthAsync()
    {
        var sw = Stopwatch.StartNew();
        var rpcUrl = this.GetRpcUrl();
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

    public async Task StartAsync()
    {
        if (this.isRunning)
        {
            return;
        }

        this.logger.Info("Starting Transmission engine backend...");

        // Ensure daemon process is running if binary is available
        await this.EnsureDaemonRunningAsync();

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
                    await Task.WhenAny(this.syncLoopTask, Task.Delay(2000));
                }
            }
            catch
            {
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
        var task = new TransmissionDownloadTask(torrent.Id, torrent.InfoHash, torrent.Name, torrent.TotalSize, torrent.Category);
        this.tasks[torrent.Id] = task;
        this.infoHashToId[torrent.InfoHash] = torrent.Id;

        try
        {
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
            else if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
            {
                addArgs["filename"] = $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}";
            }
            else
            {
                addArgs["filename"] = $"magnet:?xt=urn:btih:{torrent.InfoHash}";
            }

            var response = await this.SendRpcRequestAsync("torrent-add", addArgs);
            if (response.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement args)
            {
                if (args.TryGetProperty("torrent-added", out var added) || args.TryGetProperty("torrent-duplicate", out added))
                {
                    if (added.TryGetProperty("id", out var idProp))
                    {
                        var transmissionId = idProp.GetInt64();
                        this.torrentIdToTransmissionId[torrent.Id] = transmissionId;
                    }
                }
            }

            this.logger.Info("Transmission: Ingested torrent {0} ({1}) to {2}", torrent.Name, torrent.InfoHash, savePath);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to dispatch torrent-add to Transmission daemon for {0}. Ingested in local state.", torrent.Name);
        }

        return task;
    }

    public async Task RemoveTorrentAsync(int torrentId, bool deleteFiles)
    {
        if (this.tasks.TryRemove(torrentId, out var task))
        {
            this.infoHashToId.TryRemove(task.InfoHash, out _);

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

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        this.logger.Error("VPN Kill Switch drop detected for interface '{0}'. Halting Transmission engine transfers.", message.InterfaceName);
        this.isHaltedByKillSwitch = true;

        lock (this.torrentsHaltedByKillSwitch)
        {
            this.torrentsHaltedByKillSwitch.Clear();
            foreach (var task in this.tasks.Values)
            {
                if (task.Status == TorrentStatus.Downloading || task.Status == TorrentStatus.Seeding)
                {
                    this.torrentsHaltedByKillSwitch.Add(task.TorrentId);
                    task.Status = TorrentStatus.Paused;
                }
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await this.SendRpcRequestAsync("torrent-stop", new Dictionary<string, object>());
            }
            catch
            {
            }
        });
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        this.logger.Info("VPN interface '{0}' restored. Resuming Transmission engine transfers.", message.InterfaceName);
        this.isHaltedByKillSwitch = false;

        lock (this.torrentsHaltedByKillSwitch)
        {
            foreach (var torrentId in this.torrentsHaltedByKillSwitch)
            {
                if (this.tasks.TryGetValue(torrentId, out var task) && task.Status == TorrentStatus.Paused)
                {
                    task.Status = task.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading;
                    _ = this.ResumeTorrentAsync(torrentId);
                }
            }

            this.torrentsHaltedByKillSwitch.Clear();
        }
    }

    public void Handle(NetworkBindingProviderSwitchedEvent message)
    {
        this.logger.Info("Transmission: Network binding provider switched ({0} -> {1}). Recycling peer sessions.", message.PreviousProvider, message.NewProvider);
        _ = Task.Run(async () =>
        {
            try
            {
                await this.SendRpcRequestAsync("session-set", new Dictionary<string, object>
                {
                    ["bind-address-ipv4"] = "0.0.0.0",
                });
            }
            catch
            {
            }
        });
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

    public async Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
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
                        ["trackerAdd"] = trackers.ToArray(),
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error adding trackers in Transmission for {0}", torrentId);
            }
        }
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
                            ["fields"] = new[] { "trackers" },
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
                                        if (tId >= 0 && announce != null && nonIntTrackers.Contains(announce))
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
                    else
                    {
                        await this.SendRpcRequestAsync("torrent-set", new Dictionary<string, object>
                        {
                            ["ids"] = rpcIds,
                            ["trackerRemove"] = trackerList.ToArray(),
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
                    int fileIndex = -1;
                    if (!int.TryParse(filePath, out fileIndex))
                    {
                        var getResp = await this.SendRpcRequestAsync("torrent-get", new Dictionary<string, object>
                        {
                            ["ids"] = rpcIds,
                            ["fields"] = new[] { "files" },
                        });

                        if (getResp.TryGetValue("arguments", out var argsObj) && argsObj is JsonElement args && args.TryGetProperty("torrents", out var torrentsArray))
                        {
                            var normalized = filePath?.Replace('\\', '/').TrimStart('/');
                            foreach (var item in torrentsArray.EnumerateArray())
                            {
                                if (item.TryGetProperty("files", out var fArr))
                                {
                                    int idx = 0;
                                    foreach (var f in fArr.EnumerateArray())
                                    {
                                        var name = f.TryGetProperty("name", out var n) ? n.GetString()?.Replace('\\', '/').TrimStart('/') : null;
                                        if (name != null && (name.Equals(normalized, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase) || normalized.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase)))
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

    public async Task MoveTorrentFilesAsync(int torrentId, string newSavePath)
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
                        ["move"] = true,
                    });
                }
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error moving files for torrent {0}", torrentId);
            }
        }
    }

    public IDownloadTask GetTask(int torrentId)
    {
        this.tasks.TryGetValue(torrentId, out var task);
        return task;
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
            this.httpClient.Dispose();
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
                    "pieceCount", "pieces", "peers",
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

                            if (item.TryGetProperty("peersSendingToUs", out var seeds))
                            {
                                task.ConnectedSeeders = seeds.GetInt32();
                            }

                            if (item.TryGetProperty("peersGettingFromUs", out var leechers))
                            {
                                task.ConnectedLeechers = leechers.GetInt32();
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
                            }
                        }
                    }
                }
            }
            catch
            {
                // Daemon poll sync error, will retry on next tick
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

    private async Task<Dictionary<string, object>> SendRpcRequestAsync(string method, Dictionary<string, object> arguments)
    {
        var rpcUrl = this.GetRpcUrl();
        var payload = new Dictionary<string, object>
        {
            ["method"] = method,
            ["arguments"] = arguments,
            ["tag"] = Random.Shared.Next(1, 1000000),
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
                return retryContent.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();
            }
        }

        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return content.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();
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

    private string GetRpcUrl()
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

    private async Task EnsureDaemonRunningAsync()
    {
        var binary = GetDaemonBinaryPath();
        if (string.IsNullOrWhiteSpace(binary))
        {
            return;
        }

        try
        {
            // Test if already answering
            await this.SendRpcRequestAsync("session-get", new Dictionary<string, object>());
            return;
        }
        catch
        {
            // Start daemon child process
        }

        try
        {
            var configDir = Path.Combine(Path.GetTempPath(), "leecharr-transmission");
            Directory.CreateDirectory(configDir);

            var startInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = $"--foreground --config-dir \"{configDir}\" --port 9091 --allowed 127.0.0.1,::1",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            this.daemonProcess = Process.Start(startInfo);
            await Task.Delay(500);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to start transmission-daemon child process automatically.");
        }
    }

    private string ResolveSavePath(Torrent torrent)
    {
        var defaultCompletedDir = this.storagePathService.GetCompletedDirectory(torrent.Category);
        return this.categoryService.GetSavePathForCategory(torrent.Category, defaultCompletedDir);
    }
}

public class TransmissionDownloadTask : IDownloadTask
{
    public int TorrentId { get; }

    public string InfoHash { get; }

    public string Name { get; }

    public string Category { get; set; } = string.Empty;

    public long TotalSize { get; }

    public long TotalBytes => this.TotalSize;

    public TorrentStatus Status { get; set; } = TorrentStatus.Downloading;

    public long DownloadedBytes { get; set; }

    public long UploadedBytes { get; set; }

    public double Progress { get; set; }

    public long DownloadSpeed
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.downloadSpeed;
        set => this.downloadSpeed = value;
    }

    public long UploadSpeed
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.uploadSpeed;
        set => this.uploadSpeed = value;
    }

    public int ConnectedSeeders
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.connectedSeeders;
        set => this.connectedSeeders = value;
    }

    public int ConnectedLeechers
    {
        get => (this.Status is TorrentStatus.Paused or TorrentStatus.Stopped or TorrentStatus.Error or TorrentStatus.Queued) ? 0 : this.connectedLeechers;
        set => this.connectedLeechers = value;
    }

    private long downloadSpeed;
    private long uploadSpeed;
    private int connectedSeeders;
    private int connectedLeechers;
    private IReadOnlyList<PeerInfo> peers = Array.Empty<PeerInfo>();

    public bool[] PieceBitfield { get; set; } = Array.Empty<bool>();

    public int[] PieceAvailability { get; set; } = Array.Empty<int>();

    public TorrentResourceMetrics GetResourceMetrics() => new()
    {
        TorrentId = this.TorrentId,
        InfoHash = this.InfoHash ?? string.Empty,
        Name = this.Name ?? string.Empty,
        Category = this.Category ?? string.Empty,
        Status = this.Status.ToString() ?? "Stopped",
        Progress = this.Progress,
        TotalBytes = this.TotalSize,
        DownloadedPayload = this.DownloadedBytes,
        UploadedPayload = this.UploadedBytes,
        PayloadDownloadSpeed = this.DownloadSpeed,
        PayloadUploadSpeed = this.UploadSpeed,
        ConnectedSeeds = this.ConnectedSeeders,
        ConnectedLeechers = this.ConnectedLeechers,
        ConnectedPeers = this.ConnectedSeeders + this.ConnectedLeechers,
    };

    public TransmissionDownloadTask(int torrentId, string infoHash, string name, long totalSize, string category = null)
    {
        this.TorrentId = torrentId;
        this.InfoHash = infoHash;
        this.Name = name;
        this.TotalSize = totalSize;
        this.Category = category ?? string.Empty;
    }

    public IReadOnlyList<PeerInfo> GetPeers()
    {
        return this.peers;
    }

    public void SetPeers(IEnumerable<PeerInfo> peerList)
    {
        this.peers = peerList?.ToList() ?? (IReadOnlyList<PeerInfo>)Array.Empty<PeerInfo>();
    }
}
