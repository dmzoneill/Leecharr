// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Datastore;
using NzbDrone.Host;

namespace NzbDrone.Integration.Test;

public sealed class LeecharrWebApplicationFactory : IDisposable
{
    private readonly WebApplication app;
    private readonly string tempDir;
    private bool disposed;

    public string BaseUrl { get; }

    public string ApiKey { get; private set; } = string.Empty;

    public HttpClient Client { get; }

    public IServiceProvider Services => this.app.Services;

    private static readonly object DbResetLock = new();

    public void ResetDatabase()
    {
        lock (DbResetLock)
        {
            try
            {
                var downloadEngine = this.app.Services.GetService<IDownloadEngine>();
                if (downloadEngine != null)
                {
                    foreach (var task in downloadEngine.GetAllTasks().ToList())
                    {
                        try
                        {
                            downloadEngine.RemoveTorrentAsync(task.TorrentId, false).GetAwaiter().GetResult();
                        }
                        catch
                        {
                            // Best-effort removal of in-memory engine tasks
                        }
                    }
                }
            }
            catch
            {
                // Engine cleanup is best-effort
            }

            try
            {
                var database = this.app.Services.GetService<IMainDatabase>();
                if (database == null)
                {
                    return;
                }

                using var connection = database.OpenConnection();
                using var cmd = connection.CreateCommand();

                if (database.DatabaseType == DatabaseType.SQLite)
                {
                    cmd.CommandText =
                        "PRAGMA foreign_keys = OFF; " +
                        "DELETE FROM \"TorrentEventLogs\"; " +
                        "DELETE FROM \"TorrentFiles\"; " +
                        "DELETE FROM \"TorrentMediaMetadata\"; " +
                        "DELETE FROM \"TrackerMetrics\"; " +
                        "DELETE FROM \"TrackerMetricSnapshots\"; " +
                        "DELETE FROM \"TrackerEntries\"; " +
                        "DELETE FROM \"Torrents\"; " +
                        "DELETE FROM \"Categories\"; " +
                        "DELETE FROM \"Tags\"; " +
                        "DELETE FROM \"ArrConnectionDefinitions\"; " +
                        "DELETE FROM \"SpeedSchedules\"; " +
                        "DELETE FROM \"DownloadHistory\"; " +
                        "DELETE FROM \"NetworkSettings\"; " +
                        "DELETE FROM \"NotificationDefinitions\"; " +
                        "DELETE FROM \"IndexerDefinitions\"; " +
                        "DELETE FROM \"RssRules\"; " +
                        "DELETE FROM \"DownloadClientDefinitions\"; " +
                        "DELETE FROM \"UserSessions\"; " +
                        "DELETE FROM \"UserExternalLogins\"; " +
                        "DELETE FROM \"Users\"; " +
                        "DELETE FROM \"IdentityProviders\"; " +
                        "DELETE FROM \"TrackerBoostTrackers\"; " +
                        "DELETE FROM \"AutomationScripts\"; " +
                        "DELETE FROM \"Commands\"; " +
                        "PRAGMA foreign_keys = ON;";
                    cmd.ExecuteNonQuery();
                }
                else
                {
                    cmd.CommandText =
                        "TRUNCATE TABLE \"TorrentEventLogs\", \"TorrentFiles\", \"TorrentMediaMetadata\", " +
                        "\"TrackerMetrics\", \"TrackerMetricSnapshots\", \"TrackerEntries\", " +
                        "\"Torrents\", \"Categories\", \"Tags\", \"ArrConnectionDefinitions\", " +
                        "\"SpeedSchedules\", \"DownloadHistory\", \"NetworkSettings\", " +
                        "\"NotificationDefinitions\", \"IndexerDefinitions\", \"RssRules\", " +
                        "\"DownloadClientDefinitions\", \"UserSessions\", \"UserExternalLogins\", " +
                        "\"Users\", \"IdentityProviders\", \"TrackerBoostTrackers\", " +
                        "\"AutomationScripts\", \"Commands\" CASCADE;";
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ResetDatabase] Warning: {ex.Message}");
            }
        }
    }

    public LeecharrWebApplicationFactory()
    {
        this.tempDir = Path.Combine(
            Path.GetTempPath(),
            "leecharr-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);

        var startupContext = new StartupContext("--data=" + this.tempDir);
        this.app = Bootstrap.CreateApplication(startupContext, new[] { "http://127.0.0.1:0" });
        this.app.StartAsync().GetAwaiter().GetResult();

        var server = this.app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressesFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        this.BaseUrl = addressesFeature?.Addresses?.FirstOrDefault() ?? "http://127.0.0.1:0";

        this.LoadApiKey();
        this.Client = new HttpClient { BaseAddress = new Uri(this.BaseUrl) };
        if (!string.IsNullOrEmpty(this.ApiKey))
        {
            this.Client.DefaultRequestHeaders.Add("X-Api-Key", this.ApiKey);
        }

        this.WaitForHealthy();
    }

    private void LoadApiKey()
    {
        try
        {
            var configFile = Path.Combine(this.tempDir, "config.xml");
            if (!File.Exists(configFile))
            {
                return;
            }

            using var stream = File.OpenRead(configFile);
            var doc = System.Xml.Linq.XDocument.Load(stream);
            this.ApiKey = doc.Root?.Element("ApiKey")?.Value ?? string.Empty;
        }
        catch
        {
            // Key stays empty
        }
    }

    private void WaitForHealthy()
    {
        var healthy = false;
        for (var i = 0; i < 50; i++)
        {
            try
            {
                var response = this.Client.GetAsync("/api/v1/system/status").GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode)
                {
                    healthy = true;
                    break;
                }
            }
            catch
            {
                // Not ready yet
            }

            Thread.Sleep(100);
        }

        if (!healthy)
        {
            throw new InvalidOperationException($"Test server failed to start at {this.BaseUrl}");
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.Client.Dispose();

        try
        {
            this.app.StopAsync().GetAwaiter().GetResult();
            this.app.DisposeAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignore during shutdown
        }

        try
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, true);
            }
        }
        catch
        {
            // Ignore temp dir deletion failure
        }
    }
}
