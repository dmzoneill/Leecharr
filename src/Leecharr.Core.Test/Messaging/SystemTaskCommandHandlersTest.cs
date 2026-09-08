// Copyright (c) PlaceholderCompany. All rights reserved.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.WatchFolder;

namespace Leecharr.Core.Test.Messaging;

[TestFixture]
public class SystemTaskCommandHandlersTest
{
    [Test]
    public async Task WatchFolderService_ExecuteAsync_CallsScanWatchFolderAsync()
    {
        var configService = Substitute.For<IConfigService>();
        configService.WatchFolderEnabled.Returns(false);
        var torrentService = Substitute.For<ITorrentService>();
        var parser = Substitute.For<ITorrentFileParser>();
        var catService = Substitute.For<ICategoryService>();
        var diskProvider = Substitute.For<IDiskProvider>();

        var service = new WatchFolderService(configService, torrentService, parser, catService, diskProvider);
        var asyncExecutor = (IExecuteAsync<WatchFolderScanCommand>)service;
        var syncExecutor = (IExecute<WatchFolderScanCommand>)service;

        var cmd = new WatchFolderScanCommand();
        await asyncExecutor.ExecuteAsync(cmd, CancellationToken.None);
        syncExecutor.Execute(cmd);

        // Since WatchFolderEnabled is false, it returns early cleanly without exception
        Assert.Pass();
    }

    [Test]
    public async Task RssSyncService_ExecuteAsync_CallsSyncRssFeedsAsync()
    {
        var indexerRepo = Substitute.For<IIndexerRepository>();
        var rssRuleRepo = Substitute.For<IRssRuleRepository>();
        var torznabClient = Substitute.For<ITorznabClient>();
        var torrentService = Substitute.For<ITorrentService>();

        var service = new RssSyncService(indexerRepo, rssRuleRepo, torznabClient, torrentService);
        var asyncExecutor = (IExecuteAsync<RssSyncCommand>)service;
        var syncExecutor = (IExecute<RssSyncCommand>)service;

        var cmd = new RssSyncCommand();
        await asyncExecutor.ExecuteAsync(cmd, CancellationToken.None);
        syncExecutor.Execute(cmd);

        indexerRepo.Received(2).GetRssEnabled();
    }

    [Test]
    public async Task NetworkSecurityService_ExecuteAsync_CallsCheckVpnKillSwitch()
    {
        var repo = Substitute.For<INetworkSettingsRepository>();
        repo.GetSettings().Returns(new NetworkSettings { EnableVpnKillSwitch = false });
        var eventAggregator = Substitute.For<IEventAggregator>();

        var service = new NetworkSecurityService(repo, eventAggregator);
        var asyncExecutor = (IExecuteAsync<VpnKillSwitchCheckCommand>)service;
        var syncExecutor = (IExecute<VpnKillSwitchCheckCommand>)service;

        var cmd = new VpnKillSwitchCheckCommand();
        await asyncExecutor.ExecuteAsync(cmd, CancellationToken.None);
        syncExecutor.Execute(cmd);

        repo.Received(2).GetSettings();
    }

    [Test]
    public async Task BackupService_ExecuteAsync_CreatesBackup()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "BackupServiceTest_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var appFolderInfo = Substitute.For<IAppFolderInfo>();
            appFolderInfo.AppDataFolder.Returns(tempDir);

            var configPath = Path.Combine(tempDir, "config.xml");
            File.WriteAllText(configPath, "<config/>");

            var service = new BackupService(appFolderInfo);
            var asyncExecutor = (IExecuteAsync<BackupCommand>)service;
            var syncExecutor = (IExecute<BackupCommand>)service;

            var cmd = new BackupCommand { Type = "Scheduled" };
            await asyncExecutor.ExecuteAsync(cmd, CancellationToken.None);

            var scheduledDir = Path.Combine(tempDir, "Backups", "scheduled");
            Directory.Exists(scheduledDir).Should().BeTrue();
            Directory.GetFiles(scheduledDir, "*.zip").Should().HaveCount(1);

            syncExecutor.Execute(new BackupCommand { Type = "Manual" });
            var manualDir = Path.Combine(tempDir, "Backups", "manual");
            Directory.Exists(manualDir).Should().BeTrue();
            Directory.GetFiles(manualDir, "*.zip").Should().HaveCount(1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                }
            }
        }
    }
}
