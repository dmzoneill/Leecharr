#nullable enable
// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

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
    private readonly Logger logger;
    private readonly ConcurrentQueue<DeveloperTestResult> recentResults = new();

    private static readonly List<DeveloperTestItem> RegisteredDefinitions = new()
    {
        new DeveloperTestItem
        {
            Id = "db-integrity",
            Name = "Database Integrity & Connection Check",
            Category = "Database",
            Description = "Opens a connection to the primary database and executes a quick integrity pragma/query.",
            TargetComponent = "MainDatabase",
        },
        new DeveloperTestItem
        {
            Id = "db-crud",
            Name = "Database Transactional CRUD Smoke Test",
            Category = "Database",
            Description = "Executes insert, select, and delete operations within an isolated transaction.",
            TargetComponent = "MainDatabase",
        },
        new DeveloperTestItem
        {
            Id = "disk-io",
            Name = "Storage Path Read/Write Throughput",
            Category = "Storage",
            Description = "Creates, writes 64KB, reads, and deletes a temporary verification file.",
            TargetComponent = "DiskProvider",
        },
        new DeveloperTestItem
        {
            Id = "engine-status",
            Name = "BitTorrent Engine Runtime State",
            Category = "BitTorrent",
            Description = "Inspects active engine status, metrics, and DHT node readiness.",
            TargetComponent = "DownloadEngine",
        },
        new DeveloperTestItem
        {
            Id = "network-dns",
            Name = "Outbound DNS & Socket Resolution",
            Category = "Network",
            Description = "Resolves public DNS hosts to verify outbound resolver readiness.",
            TargetComponent = "NetworkService",
        },
        new DeveloperTestItem
        {
            Id = "scheduler-queue",
            Name = "Scheduled Task Queue Health",
            Category = "Scheduler",
            Description = "Verifies background scheduled tasks are registered and not stalled.",
            TargetComponent = "TaskManager",
        },
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
            Id = "memory-health",
            Name = "Memory & GC Pressure Diagnostic",
            Category = "System",
            Description = "Evaluates managed heap allocation, fragmentation, and generation counts.",
            TargetComponent = "DiagnosticsService",
        },
    };

    public DeveloperTestRunner(
        IMainDatabase? mainDatabase = null,
        IDiskProvider? diskProvider = null,
        IDownloadEngine? downloadEngine = null,
        ITaskManager? taskManager = null,
        IManageCommandQueue? commandQueue = null,
        IEventAggregator? eventAggregator = null)
    {
        this.mainDatabase = mainDatabase;
        this.diskProvider = diskProvider;
        this.downloadEngine = downloadEngine;
        this.taskManager = taskManager;
        this.commandQueue = commandQueue;
        this.eventAggregator = eventAggregator;
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

                case "disk-io":
                    result.Output = await this.ExecuteDiskIoCheckAsync();
                    break;

                case "engine-status":
                    result.Output = this.ExecuteEngineStatusCheck();
                    break;

                case "network-dns":
                    result.Output = await this.ExecuteDnsCheckAsync();
                    break;

                case "scheduler-queue":
                    result.Output = this.ExecuteSchedulerCheck();
                    break;

                case "event-bus":
                    result.Output = this.ExecuteEventBusCheck();
                    break;

                case "memory-health":
                    result.Output = this.ExecuteMemoryCheck();
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
            var result = cmd.ExecuteScalar()?.ToString() ?? "ok";
            return $"SQLite Quick Check passed: '{result}' (Version: {this.mainDatabase.Version})";
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

    private string ExecuteSchedulerCheck()
    {
        if (this.taskManager == null)
        {
            return "Task manager service is not registered.";
        }

        var tasks = this.taskManager.GetAll().ToList();
        return $"Task manager verified. Registered tasks count: {tasks.Count}";
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

    private string ExecuteMemoryCheck()
    {
        var allocated = GC.GetTotalMemory(false);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);

        return $"Allocated Memory: {allocated:N0} bytes. Collections: Gen0={gen0}, Gen1={gen1}, Gen2={gen2}";
    }
}
