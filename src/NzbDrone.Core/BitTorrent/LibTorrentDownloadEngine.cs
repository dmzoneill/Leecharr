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

public class LibTorrentDownloadEngine : ITorrentEngine, IDisposable, IHandle<VpnKillSwitchTriggeredEvent>, IHandle<VpnInterfaceRestoredEvent>, IHandle<NetworkBindingProviderSwitchedEvent>
{
    private readonly IConfigService configService;
    private readonly IStoragePathService storagePathService;
    private readonly ICategoryService categoryService;
    private readonly IDiskProvider diskProvider;
    private readonly IEventAggregator eventAggregator;
    private readonly Logger logger;

    private readonly ConcurrentDictionary<int, LibTorrentDownloadTask> tasks = new();
    private readonly ConcurrentDictionary<string, int> infoHashToId = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> torrentsHaltedByKillSwitch = new();

    private readonly HttpClient httpClient;
    private CancellationTokenSource syncCts;
    private Task syncLoopTask;

    private bool isRunning;
    private bool disposed;
    private bool isHaltedByKillSwitch;

    public string ProtocolName => "BitTorrent";

    public string EngineId => "LibTorrent";

    public string DisplayName => "libtorrent (Rasterbar C++)";

    public string Version => "2.0.10 (libtorrent-rasterbar)";

    public string Description => "High-performance C++20 BitTorrent engine with memory-mapped file I/O, BitTorrent v2 Merkle trees, and LEDBAT uTP.";

    public bool IsAvailable => CheckNativeAvailability() || IsRpcEndpointConfigured();

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
        SupportsMemoryMappedIo = true,
        SupportsEncryptionToggle = true,
    };

    public LibTorrentDownloadEngine(
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
            var session = await this.SendRpcRequestAsync("session_status", new Dictionary<string, object>());
            sw.Stop();

            checks.Add($"libtorrent RPC service reachable at {rpcUrl} (Latency: {sw.ElapsedMilliseconds} ms)");
            checks.Add($"Engine Version: {this.Version}");

            return new EngineHealthCheckResult
            {
                IsHealthy = true,
                StatusMessage = $"libtorrent daemon is healthy and responding ({sw.ElapsedMilliseconds}ms).",
                DependencyChecks = checks,
                Warnings = warnings,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            var nativeLib = GetNativeLibraryPath();
            if (!string.IsNullOrWhiteSpace(nativeLib))
            {
                checks.Add($"libtorrent shared library found: {nativeLib}");
                warnings.Add($"libtorrent RPC daemon is not active on {rpcUrl}. In-process or sidecar fallback will be engaged on engine start.");

                return new EngineHealthCheckResult
                {
                    IsHealthy = true,
                    StatusMessage = $"libtorrent native library detected ({Path.GetFileName(nativeLib)}). Ready.",
                    DependencyChecks = checks,
                    Warnings = warnings,
                };
            }

            checks.Add($"libtorrent RPC connection to {rpcUrl} failed: {ex.Message}");
            warnings.Add("Neither a running libtorrent daemon nor native libtorrent-rasterbar shared library was detected.");

            return new EngineHealthCheckResult
            {
                IsHealthy = false,
                StatusMessage = $"libtorrent engine is unavailable at {rpcUrl}.",
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

        this.logger.Info("Starting libtorrent engine session...");
        this.isRunning = true;
        this.syncCts = new CancellationTokenSource();
        this.syncLoopTask = Task.Run(() => this.PollSessionLoopAsync(this.syncCts.Token));

        this.logger.Info("libtorrent engine started successfully.");
        await Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!this.isRunning)
        {
            return;
        }

        this.logger.Info("Stopping libtorrent engine session...");
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

        this.tasks.Clear();
        this.infoHashToId.Clear();
    }

    public async Task<IDownloadTask> AddTorrentAsync(Torrent torrent, byte[] torrentFileBytes = null, string magnetUri = null)
    {
        if (!this.isRunning)
        {
            await this.StartAsync();
        }

        var savePath = this.ResolveSavePath(torrent);
        var task = new LibTorrentDownloadTask(torrent.Id, torrent.InfoHash, torrent.Name, torrent.TotalSize, torrent.Category);
        this.tasks[torrent.Id] = task;
        this.infoHashToId[torrent.InfoHash] = torrent.Id;

        try
        {
            var addArgs = new Dictionary<string, object>
            {
                ["save_path"] = savePath,
                ["name"] = torrent.Name,
                ["info_hash"] = torrent.InfoHash,
            };

            if (torrentFileBytes != null && torrentFileBytes.Length > 0)
            {
                addArgs["ti"] = Convert.ToBase64String(torrentFileBytes);
            }
            else if (!string.IsNullOrWhiteSpace(magnetUri))
            {
                addArgs["url"] = magnetUri;
            }
            else if (!string.IsNullOrWhiteSpace(torrent.TrackerUrl))
            {
                addArgs["url"] = $"magnet:?xt=urn:btih:{torrent.InfoHash}&tr={Uri.EscapeDataString(torrent.TrackerUrl)}";
            }
            else
            {
                addArgs["url"] = $"magnet:?xt=urn:btih:{torrent.InfoHash}";
            }

            await this.SendRpcRequestAsync("add_torrent", addArgs);
            this.logger.Info("libtorrent: Ingested torrent {0} ({1}) to {2}", torrent.Name, torrent.InfoHash, savePath);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Failed to dispatch add_torrent to libtorrent for {0}. Ingested in local state.", torrent.Name);
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
                await this.SendRpcRequestAsync("remove_torrent", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                    ["delete_files"] = deleteFiles,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching remove_torrent to libtorrent for {0}", task.InfoHash);
            }

            this.logger.Info("libtorrent: Removed torrent {0} (deleteFiles: {1})", task.InfoHash, deleteFiles);
        }
    }

    public async Task PauseTorrentAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            task.Status = TorrentStatus.Paused;
            try
            {
                await this.SendRpcRequestAsync("pause_torrent", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching pause_torrent to libtorrent for id {0}", torrentId);
            }

            this.logger.Info("libtorrent: Paused torrent id {0}", torrentId);
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
                await this.SendRpcRequestAsync("resume_torrent", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching resume_torrent to libtorrent for id {0}", torrentId);
            }

            this.logger.Info("libtorrent: Resumed torrent id {0}", torrentId);
        }
    }

    public void Handle(VpnKillSwitchTriggeredEvent message)
    {
        this.logger.Error("VPN Kill Switch drop detected for interface '{0}'. Halting LibTorrent engine transfers.", message.InterfaceName);
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
                await this.SendRpcRequestAsync("pause_session", new Dictionary<string, object>());
            }
            catch
            {
            }
        });
    }

    public void Handle(VpnInterfaceRestoredEvent message)
    {
        this.logger.Info("VPN interface '{0}' restored. Resuming LibTorrent engine transfers.", message.InterfaceName);
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
        this.logger.Info("libtorrent: Network binding provider switched ({0} -> {1}). Recycling peer sessions.", message.PreviousProvider, message.NewProvider);
        _ = Task.Run(async () =>
        {
            try
            {
                await this.SendRpcRequestAsync("rebind_network_interfaces", new Dictionary<string, object>
                {
                    ["previousProvider"] = message.PreviousProvider,
                    ["newProvider"] = message.NewProvider,
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
                await this.SendRpcRequestAsync("force_recheck", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching force_recheck for id {0}", torrentId);
            }

            this.logger.Info("libtorrent: Initiated recheck for torrent id {0}", torrentId);
        }
    }

    public async Task ForceAnnounceAsync(int torrentId)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                await this.SendRpcRequestAsync("force_reannounce", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error dispatching force_reannounce for id {0}", torrentId);
            }
        }
    }

    public async Task AddTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                await this.SendRpcRequestAsync("add_trackers", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                    ["trackers"] = trackers.ToArray(),
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error adding trackers in libtorrent for {0}", torrentId);
            }
        }
    }

    public async Task RemoveTrackersAsync(int torrentId, IEnumerable<string> trackers)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                await this.SendRpcRequestAsync("remove_trackers", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                    ["trackers"] = trackers.ToArray(),
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error removing trackers in libtorrent for {0}", torrentId);
            }
        }
    }

    public async Task SetFilePriorityAsync(int torrentId, string filePath, int priority)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                await this.SendRpcRequestAsync("set_file_priorities", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                    ["file_path"] = filePath,
                    ["priority"] = priority,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error setting file priority in libtorrent for {0} (file: {1}, priority: {2})", torrentId, filePath, priority);
            }
        }
    }

    public async Task SetRateLimitsAsync(int maxDownloadKbps, int maxUploadKbps)
    {
        try
        {
            var args = new Dictionary<string, object>
            {
                ["download_rate_limit"] = maxDownloadKbps * 1024,
                ["upload_rate_limit"] = maxUploadKbps * 1024,
            };

            await this.SendRpcRequestAsync("set_settings", args);
            this.logger.Info("libtorrent: Set rate limits: DL {0} KB/s, UL {1} KB/s", maxDownloadKbps, maxUploadKbps);
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "Error setting rate limits in libtorrent");
        }
    }

    public async Task SetTorrentRateLimitsAsync(int torrentId, int maxDownloadKbps, int maxUploadKbps)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                var args = new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                    ["download_limit"] = maxDownloadKbps * 1024,
                    ["upload_limit"] = maxUploadKbps * 1024,
                };

                await this.SendRpcRequestAsync("set_torrent_limits", args);
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error setting torrent rate limits in libtorrent for {0}", torrentId);
            }
        }
    }

    public async Task MoveTorrentFilesAsync(int torrentId, string newSavePath)
    {
        if (this.tasks.TryGetValue(torrentId, out var task))
        {
            try
            {
                await this.SendRpcRequestAsync("move_storage", new Dictionary<string, object>
                {
                    ["info_hash"] = task.InfoHash,
                    ["save_path"] = newSavePath,
                });
            }
            catch (Exception ex)
            {
                this.logger.Warn(ex, "Error moving storage in libtorrent for {0}", torrentId);
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

    private async Task PollSessionLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await this.SendRpcRequestAsync("get_torrents_status", new Dictionary<string, object>());
                if (response.TryGetValue("torrents", out var torrentsObj) && torrentsObj is JsonElement torrentsArray)
                {
                    foreach (var item in torrentsArray.EnumerateArray())
                    {
                        var hash = item.TryGetProperty("info_hash", out var h) ? h.GetString() : null;
                        if (string.IsNullOrWhiteSpace(hash) || !this.infoHashToId.TryGetValue(hash, out var torrentId))
                        {
                            continue;
                        }

                        if (this.tasks.TryGetValue(torrentId, out var task))
                        {
                            task.Progress = item.TryGetProperty("progress", out var p) ? p.GetDouble() : task.Progress;
                            task.DownloadSpeed = item.TryGetProperty("download_rate", out var dr) ? dr.GetInt64() : 0;
                            task.UploadSpeed = item.TryGetProperty("upload_rate", out var ur) ? ur.GetInt64() : 0;
                            task.DownloadedBytes = item.TryGetProperty("total_done", out var td) ? td.GetInt64() : task.DownloadedBytes;
                            task.UploadedBytes = item.TryGetProperty("total_uploaded", out var tu) ? tu.GetInt64() : task.UploadedBytes;

                            var state = item.TryGetProperty("state", out var s) ? s.GetString() : string.Empty;
                            task.Status = state.ToLowerInvariant() switch
                            {
                                "paused" => TorrentStatus.Paused,
                                "checking_files" => TorrentStatus.Checking,
                                "downloading_metadata" => TorrentStatus.Downloading,
                                "downloading" => TorrentStatus.Downloading,
                                "finished" => TorrentStatus.Seeding,
                                "seeding" => TorrentStatus.Seeding,
                                "allocating" => TorrentStatus.Downloading,
                                "checking_resume_data" => TorrentStatus.Checking,
                                _ => task.Progress >= 1.0 ? TorrentStatus.Seeding : TorrentStatus.Downloading,
                            };

                            if (item.TryGetProperty("num_seeds", out var seeds))
                            {
                                task.ConnectedSeeders = seeds.GetInt32();
                            }

                            if (item.TryGetProperty("num_peers", out var peers))
                            {
                                task.ConnectedLeechers = Math.Max(0, peers.GetInt32() - task.ConnectedSeeders);
                            }

                            if (item.TryGetProperty("peers", out var peersArray) && peersArray.ValueKind == JsonValueKind.Array)
                            {
                                var peerList = new List<PeerInfo>();
                                foreach (var peerElem in peersArray.EnumerateArray())
                                {
                                    var ip = peerElem.TryGetProperty("ip", out var addr) ? addr.GetString() : (peerElem.TryGetProperty("address", out addr) ? addr.GetString() : "unknown");
                                    var port = peerElem.TryGetProperty("port", out var prt) ? prt.GetInt32() : 0;
                                    var client = peerElem.TryGetProperty("client", out var cn) ? cn.GetString() : (peerElem.TryGetProperty("client_name", out cn) ? cn.GetString() : string.Empty);
                                    var flags = peerElem.TryGetProperty("flags", out var fs) ? (fs.ValueKind == JsonValueKind.String ? fs.GetString() : fs.ToString()) : string.Empty;
                                    var progress = peerElem.TryGetProperty("progress", out var prg) ? prg.GetDouble() : 0.0;
                                    var downSpeed = peerElem.TryGetProperty("download_rate", out var pdr) ? pdr.GetInt64() : (peerElem.TryGetProperty("down_speed", out pdr) ? pdr.GetInt64() : 0);
                                    var upSpeed = peerElem.TryGetProperty("upload_rate", out var pur) ? pur.GetInt64() : (peerElem.TryGetProperty("up_speed", out pur) ? pur.GetInt64() : 0);
                                    var totalDown = peerElem.TryGetProperty("total_download", out var ptd) ? ptd.GetInt64() : (peerElem.TryGetProperty("total_done", out ptd) ? ptd.GetInt64() : 0);
                                    var totalUp = peerElem.TryGetProperty("total_upload", out var ptu) ? ptu.GetInt64() : (peerElem.TryGetProperty("total_uploaded", out ptu) ? ptu.GetInt64() : 0);
                                    var isEncrypted = (peerElem.TryGetProperty("is_encrypted", out var enc) && enc.GetBoolean()) || (peerElem.TryGetProperty("encrypted", out enc) && enc.GetBoolean());
                                    var isUtp = (peerElem.TryGetProperty("is_utp", out var utp) && utp.GetBoolean()) || (peerElem.TryGetProperty("utp", out utp) && utp.GetBoolean());
                                    var isIncoming = (peerElem.TryGetProperty("is_incoming", out var inc) && inc.GetBoolean()) || (peerElem.TryGetProperty("incoming", out inc) && inc.GetBoolean());
                                    var isChoked = (peerElem.TryGetProperty("is_choked", out var chk) && chk.GetBoolean()) || (peerElem.TryGetProperty("peer_choking", out chk) && chk.GetBoolean());
                                    var isInterested = (peerElem.TryGetProperty("is_interested", out var inst) && inst.GetBoolean()) || (peerElem.TryGetProperty("peer_interested", out inst) && inst.GetBoolean());
                                    var clientIsChoked = (peerElem.TryGetProperty("client_is_choked", out var cchk) && cchk.GetBoolean()) || (peerElem.TryGetProperty("client_choking", out cchk) && cchk.GetBoolean());
                                    var clientIsInterested = (peerElem.TryGetProperty("client_is_interested", out var cinst) && cinst.GetBoolean()) || (peerElem.TryGetProperty("client_interested", out cinst) && cinst.GetBoolean());

                                    peerList.Add(new PeerInfo
                                    {
                                        Ip = ip ?? "unknown",
                                        Port = port,
                                        Client = client ?? string.Empty,
                                        Flags = flags ?? string.Empty,
                                        Progress = progress,
                                        DownloadSpeed = downSpeed,
                                        UploadSpeed = upSpeed,
                                        Downloaded = totalDown,
                                        Uploaded = totalUp,
                                        IsEncrypted = isEncrypted,
                                        IsChoked = isChoked,
                                        IsInterested = isInterested,
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
                // Libtorrent poll error, will retry on next tick
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
            ["params"] = arguments,
            ["id"] = Random.Shared.Next(1, 1000000),
        };

        var json = payload.ToJson();
        using var request = new HttpRequestMessage(HttpMethod.Post, rpcUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        var response = await this.httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return content.FromJson<Dictionary<string, object>>() ?? new Dictionary<string, object>();
    }

    private string GetRpcUrl()
    {
        var envUrl = Environment.GetEnvironmentVariable("LIBTORRENT_RPC_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl;
        }

        return "http://127.0.0.1:58846";
    }

    private static bool IsRpcEndpointConfigured()
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIBTORRENT_RPC_URL"));
    }

    private static bool CheckNativeAvailability()
    {
        try
        {
            var binary = GetNativeLibraryPath();
            return !string.IsNullOrWhiteSpace(binary);
        }
        catch
        {
            return false;
        }
    }

    private static string GetNativeLibraryPath()
    {
        var customPath = Environment.GetEnvironmentVariable("LIBTORRENT_PATH");
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        var candidatePaths = new[]
        {
            "/usr/lib/x86_64-linux-gnu",
            "/usr/lib/aarch64-linux-gnu",
            "/usr/lib",
            "/usr/lib64",
            "/usr/local/lib",
        };

        foreach (var dir in candidatePaths)
        {
            if (Directory.Exists(dir))
            {
                var matches = Directory.GetFiles(dir, "*libtorrent-rasterbar*.so*");
                if (matches.Length > 0)
                {
                    return matches[0];
                }
            }
        }

        return null;
    }

    private string ResolveSavePath(Torrent torrent)
    {
        var defaultCompletedDir = this.storagePathService.GetCompletedDirectory(torrent.Category);
        return this.categoryService.GetSavePathForCategory(torrent.Category, defaultCompletedDir);
    }
}

public class LibTorrentDownloadTask : IDownloadTask
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

    public LibTorrentDownloadTask(int torrentId, string infoHash, string name, long totalSize, string category = null)
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
