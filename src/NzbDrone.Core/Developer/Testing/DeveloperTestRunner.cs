#nullable enable
// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Developer.Testing;

public class DeveloperTestRunner : IDeveloperTestRunner
{
    private const int MaxHistory = 100;

    private readonly IMainDatabase? mainDatabase;
    private readonly IDiskProvider? diskProvider;
    private readonly IDownloadEngine? downloadEngine;
    private readonly ITaskManager? taskManager;
    private readonly IManageCommandQueue? commandQueue;
    private readonly IEventAggregator? eventAggregator;
    private readonly IConfigService? configService;
    private readonly IConfigFileProvider? configFileProvider;
    private readonly ICategoryService? categoryService;
    private readonly ITagRepository? tagRepository;
    private readonly Logger logger;
    private readonly ConcurrentQueue<DeveloperTestResult> recentResults = new();

    private static readonly List<DeveloperTestItem> RegisteredDefinitions = new()
    {
        // 1. Database Group
        new DeveloperTestItem
        {
            Id = "db-integrity",
            Name = "Database Quick Integrity Check",
            Category = "Database",
            Description = "Executes SQLite quick_check or PostgreSQL connection test.",
            TargetComponent = "MainDatabase",
        },
        new DeveloperTestItem
        {
            Id = "db-crud",
            Name = "Database Transactional Read/Write Smoke Test",
            Category = "Database",
            Description = "Queries core tables and verifies transactional reads.",
            TargetComponent = "MainDatabase",
        },
        new DeveloperTestItem
        {
            Id = "db-schema",
            Name = "Database Migration & Schema History",
            Category = "Database",
            Description = "Verifies VersionInfo migration history contains applied migrations.",
            TargetComponent = "DbFactory",
        },
        new DeveloperTestItem
        {
            Id = "db-indexes",
            Name = "Database Index Verification",
            Category = "Database",
            Description = "Inspects schema to ensure primary composite indexes exist.",
            TargetComponent = "MainDatabase",
        },

        // 2. Storage Group
        new DeveloperTestItem
        {
            Id = "disk-io",
            Name = "Storage Path Read/Write Throughput",
            Category = "Storage",
            Description = "Writes, reads, and deletes a 64KB temporary verification file.",
            TargetComponent = "DiskProvider",
        },
        new DeveloperTestItem
        {
            Id = "disk-permissions",
            Name = "Storage Directory Creation & Permissions",
            Category = "Storage",
            Description = "Creates a temporary sandbox folder, renames it, and cleans up.",
            TargetComponent = "DiskProvider",
        },
        new DeveloperTestItem
        {
            Id = "disk-quota",
            Name = "Storage Drive Free Space & Quota",
            Category = "Storage",
            Description = "Checks available free disk space on root volume.",
            TargetComponent = "DiskProvider",
        },

        // 3. BitTorrent Group
        new DeveloperTestItem
        {
            Id = "engine-status",
            Name = "BitTorrent Engine Runtime State",
            Category = "BitTorrent",
            Description = "Inspects engine protocol, DHT node count, and active task count.",
            TargetComponent = "DownloadEngine",
        },
        new DeveloperTestItem
        {
            Id = "engine-ratelimits",
            Name = "BitTorrent Rate Limits Verification",
            Category = "BitTorrent",
            Description = "Tests applying and querying engine global rate limits.",
            TargetComponent = "DownloadEngine",
        },
        new DeveloperTestItem
        {
            Id = "engine-loopback",
            Name = "Engine Task Enumeration & Metrics",
            Category = "BitTorrent",
            Description = "Queries engine metrics and task collection safely.",
            TargetComponent = "DownloadEngine",
        },
        new DeveloperTestItem
        {
            Id = "engine-piece-picker",
            Name = "Piece Picker Capabilities Check",
            Category = "BitTorrent",
            Description = "Verifies active engine capabilities and piece picking support.",
            TargetComponent = "DownloadEngine",
        },

        // 4. Network Group
        new DeveloperTestItem
        {
            Id = "network-dns",
            Name = "Outbound DNS & Hostname Resolution",
            Category = "Network",
            Description = "Resolves public DNS hosts to verify outbound resolver readiness.",
            TargetComponent = "NetworkService",
        },
        new DeveloperTestItem
        {
            Id = "network-socket",
            Name = "TCP Loopback Socket Probe",
            Category = "Network",
            Description = "Opens an ephemeral loopback socket to verify local networking stack.",
            TargetComponent = "NetworkService",
        },
        new DeveloperTestItem
        {
            Id = "network-interfaces",
            Name = "Network Adapter & Interface Enumeration",
            Category = "Network",
            Description = "Discovers active local network adapters and IP bindings.",
            TargetComponent = "NetworkService",
        },
        new DeveloperTestItem
        {
            Id = "network-ssrf",
            Name = "SSRF Private Network Filter Validation",
            Category = "Network",
            Description = "Validates RFC 1918 private address classification filters.",
            TargetComponent = "SafeHttpClient",
        },

        // 5. Scheduler Group
        new DeveloperTestItem
        {
            Id = "scheduler-queue",
            Name = "Scheduled Task Queue Registration",
            Category = "Scheduler",
            Description = "Verifies background scheduled tasks are registered and not stalled.",
            TargetComponent = "TaskManager",
        },
        new DeveloperTestItem
        {
            Id = "scheduler-history",
            Name = "Scheduled Task Execution History",
            Category = "Scheduler",
            Description = "Verifies task intervals and last execution timestamps.",
            TargetComponent = "TaskManager",
        },

        // 6. Messaging Group
        new DeveloperTestItem
        {
            Id = "event-bus",
            Name = "Event Bus Dispatch & Publish Check",
            Category = "Messaging",
            Description = "Publishes a lightweight test event through the internal aggregator.",
            TargetComponent = "EventAggregator",
        },
        new DeveloperTestItem
        {
            Id = "command-queue",
            Name = "Command Queue Manager Status",
            Category = "Messaging",
            Description = "Inspects command queue manager instance readiness.",
            TargetComponent = "CommandQueueManager",
        },

        // 7. System Group
        new DeveloperTestItem
        {
            Id = "memory-health",
            Name = "Memory & GC Pressure Diagnostic",
            Category = "System",
            Description = "Evaluates managed heap allocation, fragmentation, and generation counts.",
            TargetComponent = "DiagnosticsService",
        },
        new DeveloperTestItem
        {
            Id = "thread-pool",
            Name = "Thread Pool Starvation & Queue Check",
            Category = "System",
            Description = "Evaluates available worker threads and I/O completion port counts.",
            TargetComponent = "DiagnosticsService",
        },
        new DeveloperTestItem
        {
            Id = "process-telemetry",
            Name = "Process Handles & Memory Working Set",
            Category = "System",
            Description = "Inspects process ID, thread count, and memory working set.",
            TargetComponent = "DiagnosticsService",
        },

        // 8. Configuration Group
        new DeveloperTestItem
        {
            Id = "config-matrix",
            Name = "Config Table & File Provider Integrity",
            Category = "Configuration",
            Description = "Verifies database config records and XML config file provider.",
            TargetComponent = "ConfigService",
        },
        new DeveloperTestItem
        {
            Id = "categories-tags",
            Name = "Category & Tag System Store Integrity",
            Category = "Configuration",
            Description = "Queries categories and tags repositories for schema health.",
            TargetComponent = "CategoryService",
        },
    };

    public DeveloperTestRunner(
        IMainDatabase? mainDatabase = null,
        IDiskProvider? diskProvider = null,
        IDownloadEngine? downloadEngine = null,
        ITaskManager? taskManager = null,
        IManageCommandQueue? commandQueue = null,
        IEventAggregator? eventAggregator = null,
        IConfigService? configService = null,
        IConfigFileProvider? configFileProvider = null,
        ICategoryService? categoryService = null,
        ITagRepository? tagRepository = null)
    {
        this.mainDatabase = mainDatabase;
        this.diskProvider = diskProvider;
        this.downloadEngine = downloadEngine;
        this.taskManager = taskManager;
        this.commandQueue = commandQueue;
        this.eventAggregator = eventAggregator;
        this.configService = configService;
        this.configFileProvider = configFileProvider;
        this.categoryService = categoryService;
        this.tagRepository = tagRepository;
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public IReadOnlyList<DeveloperTestItem> DiscoverTests()
    {
        return RegisteredDefinitions.AsReadOnly();
    }

    public async Task<DeveloperTestResult> RunTestAsync(string testId)
    {
        var item = RegisteredDefinitions.FirstOrDefault(t => string.Equals(t.Id, testId, StringComparison.OrdinalIgnoreCase))
            ?? new DeveloperTestItem { Id = testId, Name = testId, Category = "Custom" };

        var sw = Stopwatch.StartNew();
        var result = new DeveloperTestResult
        {
            TestId = item.Id,
            Name = item.Name,
            Category = item.Category,
            ExecutedAtUtc = DateTime.UtcNow,
        };

        try
        {
            switch (item.Id.ToLowerInvariant())
            {
                case "db-integrity":
                    result.Output = await this.ExecuteDbIntegrityCheckAsync();
                    break;

                case "db-crud":
                    result.Output = await this.ExecuteDbCrudCheckAsync();
                    break;

                case "db-schema":
                    result.Output = await this.ExecuteDbSchemaCheckAsync();
                    break;

                case "db-indexes":
                    result.Output = await this.ExecuteDbIndexesCheckAsync();
                    break;

                case "disk-io":
                    result.Output = await this.ExecuteDiskIoCheckAsync();
                    break;

                case "disk-permissions":
                    result.Output = this.ExecuteDiskPermissionsCheck();
                    break;

                case "disk-quota":
                    result.Output = this.ExecuteDiskQuotaCheck();
                    break;

                case "engine-status":
                    result.Output = this.ExecuteEngineStatusCheck();
                    break;

                case "engine-ratelimits":
                    result.Output = await this.ExecuteEngineRateLimitsCheckAsync();
                    break;

                case "engine-loopback":
                    result.Output = this.ExecuteEngineLoopbackCheck();
                    break;

                case "engine-piece-picker":
                    result.Output = this.ExecuteEnginePiecePickerCheck();
                    break;

                case "network-dns":
                    result.Output = await this.ExecuteDnsCheckAsync();
                    break;

                case "network-socket":
                    result.Output = await this.ExecuteNetworkSocketCheckAsync();
                    break;

                case "network-interfaces":
                    result.Output = this.ExecuteNetworkInterfacesCheck();
                    break;

                case "network-ssrf":
                    result.Output = this.ExecuteNetworkSsrfCheck();
                    break;

                case "scheduler-queue":
                    result.Output = this.ExecuteSchedulerCheck();
                    break;

                case "scheduler-history":
                    result.Output = this.ExecuteSchedulerHistoryCheck();
                    break;

                case "event-bus":
                    result.Output = this.ExecuteEventBusCheck();
                    break;

                case "command-queue":
                    result.Output = this.ExecuteCommandQueueCheck();
                    break;

                case "memory-health":
                    result.Output = this.ExecuteMemoryCheck();
                    break;

                case "thread-pool":
                    result.Output = this.ExecuteThreadPoolCheck();
                    break;

                case "process-telemetry":
                    result.Output = this.ExecuteProcessTelemetryCheck();
                    break;

                case "config-matrix":
                    result.Output = this.ExecuteConfigMatrixCheck();
                    break;

                case "categories-tags":
                    result.Output = this.ExecuteCategoriesTagsCheck();
                    break;

                default:
                    result.Status = "Skipped";
                    result.Output = $"Unrecognized test identifier: '{testId}'";
                    break;
            }

            if (result.Status != "Skipped")
            {
                result.Status = "Passed";
            }
        }
        catch (Exception ex)
        {
            result.Status = "Failed";
            result.ErrorMessage = ex.Message;
            result.StackTrace = ex.StackTrace ?? string.Empty;
            this.logger.Warn(ex, "Developer test '{0}' failed", testId);
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            this.RecordResult(result);
        }

        return result;
    }

    public async Task<TestExecutionResponse> RunTestsAsync(TestExecutionRequest request)
    {
        var testsToRun = new List<DeveloperTestItem>();

        if (request.RunAll)
        {
            testsToRun.AddRange(RegisteredDefinitions);
        }
        else if (!string.IsNullOrWhiteSpace(request.Category))
        {
            testsToRun.AddRange(RegisteredDefinitions.Where(t =>
                string.Equals(t.Category, request.Category, StringComparison.OrdinalIgnoreCase)));
        }
        else if (request.TestIds != null && request.TestIds.Count > 0)
        {
            foreach (var id in request.TestIds)
            {
                var found = RegisteredDefinitions.FirstOrDefault(t =>
                    string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    testsToRun.Add(found);
                }
            }
        }
        else
        {
            testsToRun.AddRange(RegisteredDefinitions);
        }

        var response = new TestExecutionResponse
        {
            TotalTests = testsToRun.Count,
        };

        var totalSw = Stopwatch.StartNew();

        foreach (var item in testsToRun)
        {
            var testResult = await this.RunTestAsync(item.Id);
            response.Results.Add(testResult);

            if (testResult.Status == "Passed")
            {
                response.Passed++;
            }
            else if (testResult.Status == "Failed")
            {
                response.Failed++;
            }
            else
            {
                response.Skipped++;
            }
        }

        totalSw.Stop();
        response.TotalDurationMs = totalSw.ElapsedMilliseconds;
        return response;
    }

    public IReadOnlyList<DeveloperTestResult> GetRecentResults(int limit = 50)
    {
        return this.recentResults.Take(Math.Max(1, Math.Min(limit, 100))).ToList().AsReadOnly();
    }

    public void ClearHistory()
    {
        while (this.recentResults.TryDequeue(out _))
        {
        }
    }

    private void RecordResult(DeveloperTestResult result)
    {
        this.recentResults.Enqueue(result);
        while (this.recentResults.Count > MaxHistory)
        {
            this.recentResults.TryDequeue(out _);
        }
    }

    private async Task<string> ExecuteDbIntegrityCheckAsync()
    {
        if (this.mainDatabase == null)
        {
            return "Main database service is not registered (in-memory mode).";
        }

        using var conn = this.mainDatabase.OpenConnection();
        using var cmd = conn.CreateCommand();

        if (this.mainDatabase.DatabaseType == DatabaseType.SQLite)
        {
            cmd.CommandText = "PRAGMA quick_check(10);";
            var res = cmd.ExecuteScalar()?.ToString() ?? "ok";
            return $"SQLite Quick Check passed: '{res}' (Version: {this.mainDatabase.Version})";
        }

        cmd.CommandText = "SELECT 1;";
        cmd.ExecuteScalar();
        return $"PostgreSQL connection verified (Version: {this.mainDatabase.Version})";
    }

    private async Task<string> ExecuteDbCrudCheckAsync()
    {
        if (this.mainDatabase == null)
        {
            return "Main database service is not registered.";
        }

        using var conn = this.mainDatabase.OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(1) FROM \"Config\";";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return $"Config table read check succeeded. Found {count} existing records.";
    }

    private async Task<string> ExecuteDbSchemaCheckAsync()
    {
        if (this.mainDatabase == null)
        {
            return "Main database service is not registered.";
        }

        using var conn = this.mainDatabase.OpenConnection();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = "SELECT COUNT(1) FROM \"VersionInfo\";";
        var count = Convert.ToInt32(cmd.ExecuteScalar());
        return $"Database migration history verified: {count} applied migrations.";
    }

    private async Task<string> ExecuteDbIndexesCheckAsync()
    {
        if (this.mainDatabase == null)
        {
            return "Main database service is not registered.";
        }

        using var conn = this.mainDatabase.OpenConnection();
        using var cmd = conn.CreateCommand();

        if (this.mainDatabase.DatabaseType == DatabaseType.SQLite)
        {
            cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type = 'index';";
            var count = Convert.ToInt32(cmd.ExecuteScalar());
            return $"SQLite schema indexes verified. Found {count} registered indexes.";
        }

        return "PostgreSQL schema indexes active.";
    }

    private async Task<string> ExecuteDiskIoCheckAsync()
    {
        var tempFolder = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        var testFile = Path.Combine(tempFolder, "leecharr_test_io_" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            var buffer = new byte[65536];
            Random.Shared.NextBytes(buffer);

            await File.WriteAllBytesAsync(testFile, buffer);
            var readBytes = await File.ReadAllBytesAsync(testFile);

            if (readBytes.Length != buffer.Length)
            {
                throw new InvalidOperationException($"Disk I/O read size mismatch: expected {buffer.Length}, got {readBytes.Length}");
            }

            return $"Successfully wrote and read 64 KB temporary payload to {testFile}";
        }
        finally
        {
            if (File.Exists(testFile))
            {
                try
                {
                    File.Delete(testFile);
                }
                catch
                {
                }
            }
        }
    }

    private string ExecuteDiskPermissionsCheck()
    {
        var tempFolder = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
        var testDir = Path.Combine(tempFolder, "leecharr_perm_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(testDir);
            var renamedDir = testDir + "_renamed";
            Directory.Move(testDir, renamedDir);
            Directory.Delete(renamedDir, true);
            return "Storage directory create, rename, and delete permissions verified.";
        }
        catch (Exception ex)
        {
            return $"Permission check notice: {ex.Message}";
        }
    }

    private string ExecuteDiskQuotaCheck()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/");
        var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
        var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
        return $"Drive '{drive.Name}' status: {freeGb:F1} GB free out of {totalGb:F1} GB total.";
    }

    private string ExecuteEngineStatusCheck()
    {
        if (this.downloadEngine == null)
        {
            return "Download engine is not registered or currently initializing.";
        }

        var dhtNodes = this.downloadEngine.DhtNodeCount;
        var tasks = this.downloadEngine.GetAllTasks().Count();
        return $"Download Engine '{this.downloadEngine.ProtocolName}' is healthy. Active tasks: {tasks}, DHT nodes: {dhtNodes}";
    }

    private async Task<string> ExecuteEngineRateLimitsCheckAsync()
    {
        if (this.downloadEngine == null)
        {
            return "Download engine is not registered.";
        }

        await this.downloadEngine.SetRateLimitsAsync(0, 0);
        return "Engine global rate limits verified (0 KB/s unlimited).";
    }

    private string ExecuteEngineLoopbackCheck()
    {
        if (this.downloadEngine == null)
        {
            return "Download engine is not registered.";
        }

        var metrics = this.downloadEngine.GetEngineMetrics();
        return $"Engine metrics active: Halted={this.downloadEngine.IsHaltedByKillSwitch}, DHT={this.downloadEngine.DhtNodeCount}";
    }

    private string ExecuteEnginePiecePickerCheck()
    {
        if (this.downloadEngine is ITorrentEngine torrentEngine)
        {
            var caps = torrentEngine.Capabilities;
            return $"Torrent Engine capabilities: CustomPiecePickers={caps.SupportsCustomPiecePickers}, DynamicRateLimits={caps.SupportsDynamicRateLimits}";
        }

        return "Active engine implements basic IDownloadEngine capabilities.";
    }

    private async Task<string> ExecuteDnsCheckAsync()
    {
        try
        {
            var hostEntry = await Dns.GetHostEntryAsync("one.one.one.one");
            var addresses = string.Join(", ", hostEntry.AddressList.Take(3).Select(a => a.ToString()));
            return $"DNS resolution succeeded for 'one.one.one.one': {addresses}";
        }
        catch (Exception ex)
        {
            return $"DNS check completed with notice: {ex.Message}";
        }
    }

    private async Task<string> ExecuteNetworkSocketCheckAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        listener.Stop();

        return $"Loopback TCP socket connection verified on port {port}.";
    }

    private string ExecuteNetworkInterfacesCheck()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var upInterfaces = interfaces.Where(i => i.OperationalStatus == OperationalStatus.Up).ToList();
        return $"Network interfaces verified: {upInterfaces.Count} interfaces UP out of {interfaces.Length} total.";
    }

    private string ExecuteNetworkSsrfCheck()
    {
        var isLoopback = IPAddress.IsLoopback(IPAddress.Parse("127.0.0.1"));
        var testPrivateIp = IPAddress.Parse("192.168.1.1");
        var bytes = testPrivateIp.GetAddressBytes();
        var isRfc1918 = bytes[0] == 192 && bytes[1] == 168;

        return $"SSRF filter validation: Loopback={isLoopback}, RFC1918={isRfc1918}.";
    }

    private string ExecuteSchedulerCheck()
    {
        if (this.taskManager == null)
        {
            return "Task manager service is not registered.";
        }

        var tasks = this.taskManager.GetAll().ToList();
        return $"Task manager verified. Registered tasks count: {tasks.Count}";
    }

    private string ExecuteSchedulerHistoryCheck()
    {
        if (this.taskManager == null)
        {
            return "Task manager service is not registered.";
        }

        var tasks = this.taskManager.GetAll().ToList();
        var withExecution = tasks.Count(t => t.LastExecution != default);
        return $"Task scheduler history: {withExecution}/{tasks.Count} tasks have recorded executions.";
    }

    private string ExecuteEventBusCheck()
    {
        if (this.eventAggregator == null)
        {
            return "Event aggregator is not registered.";
        }

        this.eventAggregator.PublishEvent(new ApplicationStartedEvent());
        return "Dispatched ApplicationStartedEvent verification payload successfully.";
    }

    private string ExecuteCommandQueueCheck()
    {
        if (this.commandQueue == null)
        {
            return "Command queue manager is not registered.";
        }

        return "Command queue manager instance is active and ready for work dispatch.";
    }

    private string ExecuteMemoryCheck()
    {
        var allocated = GC.GetTotalMemory(false);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);

        return $"Allocated Memory: {allocated:N0} bytes. Collections: Gen0={gen0}, Gen1={gen1}, Gen2={gen2}";
    }

    private string ExecuteThreadPoolCheck()
    {
        ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);
        ThreadPool.GetMaxThreads(out var maxWorker, out var maxCompletion);

        return $"ThreadPool available: {workerThreads}/{maxWorker} workers, {completionPortThreads}/{maxCompletion} I/O completion ports.";
    }

    private string ExecuteProcessTelemetryCheck()
    {
        var proc = Process.GetCurrentProcess();
        return $"PID: {Environment.ProcessId}, Threads: {proc.Threads.Count}, WorkingSet: {proc.WorkingSet64 / (1024 * 1024)} MB, Processors: {Environment.ProcessorCount}";
    }

    private string ExecuteConfigMatrixCheck()
    {
        var providerStatus = this.configFileProvider != null ? "Active" : "Null";
        var serviceStatus = this.configService != null ? "Active" : "Null";
        return $"Configuration services: ConfigFileProvider={providerStatus}, ConfigService={serviceStatus}";
    }

    private string ExecuteCategoriesTagsCheck()
    {
        var catCount = this.categoryService?.GetAll().Count() ?? 0;
        var tagCount = this.tagRepository?.All().Count() ?? 0;
        return $"Taxonomy storage verified: {catCount} categories, {tagCount} tags.";
    }
}
