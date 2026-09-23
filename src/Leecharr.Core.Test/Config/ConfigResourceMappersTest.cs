// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class ConfigResourceMappersTest
{
    private IConfigService configService = null!;

    [SetUp]
    public void SetUp()
    {
        this.configService = Substitute.For<IConfigService>();
    }

    // ---------------------------------------------------------
    // BitTorrentConfigResource & BitTorrentConfigController
    // ---------------------------------------------------------

    [Test]
    public void BitTorrentConfigResource_DefaultValues_AreCorrect()
    {
        var resource = new BitTorrentConfigResource();

        resource.EnableBep27PrivateTorrents.Should().BeTrue();
        resource.AutoRecheckOnCompletion.Should().BeTrue();
        resource.LowDiskSpaceThresholdMb.Should().Be(500);
        resource.MaxTorrentFileSizeBytes.Should().Be(250L * 1024 * 1024);
        resource.IncompleteExtension.Should().Be(".!leech");
        resource.GlobalShareLimitAction.Should().Be("Pause");
        resource.AutoShutdownAction.Should().Be("None");
        resource.AutoShutdownCondition.Should().Be("None");
    }

    [Test]
    public void BitTorrentConfigResource_DiskPreAllocationMode_AliasesPreallocationMode()
    {
        var resource = new BitTorrentConfigResource();
        resource.DiskPreAllocationMode = "Sparse";

        resource.PreallocationMode.Should().Be("Sparse");
        resource.DiskPreAllocationMode.Should().Be("Sparse");

        resource.PreallocationMode = "Full";
        resource.DiskPreAllocationMode.Should().Be("Full");
    }

    [Test]
    public void BitTorrentConfigResourceMapper_ToResource_MapsAllProperties()
    {
        this.configService.ActiveTorrentEngine.Returns("MonoTorrent");
        this.configService.EnableDht.Returns(true);
        this.configService.EnablePex.Returns(true);
        this.configService.EnableLpd.Returns(true);
        this.configService.EnableBep27PrivateTorrents.Returns(true);
        this.configService.EncryptionMode.Returns("PreferEncryption");
        this.configService.BitTorrentUserAgent.Returns("Leecharr/1.0");
        this.configService.PeerIdPrefix.Returns("-LC1000-");
        this.configService.AnnounceIntervalSeconds.Returns(1800);
        this.configService.MinAnnounceIntervalSeconds.Returns(300);
        this.configService.ScrapeIntervalSeconds.Returns(1200);

        this.configService.DownloadDir.Returns("/custom/downloads");
        this.configService.IncompleteDownloadDir.Returns("/custom/incomplete");
        this.configService.EnableIncompleteDir.Returns(true);
        this.configService.AutoRecheckOnCompletion.Returns(true);
        this.configService.PreallocationMode.Returns("Full");
        this.configService.RenamePartialFiles.Returns(true);
        this.configService.Umask.Returns("022");
        this.configService.LowDiskSpaceThresholdMb.Returns(1000);
        this.configService.MaxTorrentFileSizeBytes.Returns(100L * 1024 * 1024);

        this.configService.DownloadQueueSize.Returns(10);
        this.configService.SeedQueueSize.Returns(20);
        this.configService.QueueStalledEnabled.Returns(true);
        this.configService.QueueStalledMinutes.Returns(15);
        this.configService.IdleSeedingLimitMinutes.Returns(60);
        this.configService.IncompleteExtension.Returns(".part");
        this.configService.GlobalShareLimitAction.Returns("Stop");
        this.configService.AutoShutdownAction.Returns("Hibernate");
        this.configService.AutoShutdownCondition.Returns("OnDownloadsComplete");

        this.configService.NetworkInterfaceBinding.Returns("eth0");
        this.configService.MaxConnectionsPerIp.Returns(8);
        this.configService.MaximumHalfOpenConnections.Returns(50);
        this.configService.AnonymousMode.Returns(true);
        this.configService.ForceProxy.Returns(true);
        this.configService.PeerDscp.Returns(4);
        this.configService.PeerPortRandomOnStart.Returns(true);
        this.configService.PeerPortRandomLow.Returns(50000);
        this.configService.PeerPortRandomHigh.Returns(55000);

        this.configService.DiskCacheBytes.Returns(64L * 1024 * 1024);
        this.configService.DiskCachePolicy.Returns("Fifo");
        this.configService.FastResumeMode.Returns("Accurate");
        this.configService.AutoSaveFastResumeIntervalSeconds.Returns(300);
        this.configService.AutoSaveLoadMagnetMetadata.Returns(true);
        this.configService.AutoSaveLoadDhtCache.Returns(true);
        this.configService.PiecePickerStrategy.Returns("RarestFirst");
        this.configService.EndGamePickerEnabled.Returns(true);
        this.configService.StaleRequestTimeoutSeconds.Returns(20);
        this.configService.WebSeedDelaySeconds.Returns(30);
        this.configService.MaximumDiskReadRateKbps.Returns(50000);
        this.configService.MaximumDiskWriteRateKbps.Returns(60000);

        this.configService.HashingThreads.Returns(4);
        this.configService.AioThreads.Returns(8);
        this.configService.DiskIoWriteMode.Returns("Asynchronous");
        this.configService.DiskIoReadMode.Returns("Asynchronous");
        this.configService.FilePoolSize.Returns(128);
        this.configService.ChokingAlgorithm.Returns("FixedSlots");
        this.configService.SeedChokingAlgorithm.Returns("RoundRobin");
        this.configService.MixedModeAlgorithm.Returns("PreferTcp");
        this.configService.AlertMask.Returns("All");

        this.configService.ScriptTorrentDoneFilename.Returns("/scripts/done.sh");
        this.configService.ScriptTorrentAddedFilename.Returns("/scripts/added.sh");
        this.configService.ScriptTorrentDoneSeedingFilename.Returns("/scripts/seeding_done.sh");
        this.configService.PrefetchEnabled.Returns(true);
        this.configService.ScrapePausedTorrentsEnabled.Returns(true);
        this.configService.RpcWhitelistEnabled.Returns(true);
        this.configService.RpcWhitelist.Returns("127.0.0.1");

        this.configService.OnDownloadCompleteScript.Returns("/scripts/complete.sh");
        this.configService.OnSeedGoalReachedScript.Returns("/scripts/seed_goal.sh");
        this.configService.DefaultTrackers.Returns("udp://tracker.open.org:1337");
        this.configService.DhtBootstrapNodes.Returns("dht.transmissionbt.com:6881");

        var resource = BitTorrentConfigResourceMapper.ToResource(this.configService);

        resource.ActiveTorrentEngine.Should().Be("MonoTorrent");
        resource.EnableDht.Should().BeTrue();
        resource.EnablePex.Should().BeTrue();
        resource.EnableLpd.Should().BeTrue();
        resource.EnableBep27PrivateTorrents.Should().BeTrue();
        resource.EncryptionMode.Should().Be("PreferEncryption");
        resource.BitTorrentUserAgent.Should().Be("Leecharr/1.0");
        resource.PeerIdPrefix.Should().Be("-LC1000-");
        resource.AnnounceIntervalSeconds.Should().Be(1800);
        resource.MinAnnounceIntervalSeconds.Should().Be(300);
        resource.ScrapeIntervalSeconds.Should().Be(1200);

        resource.DownloadDir.Should().Be("/custom/downloads");
        resource.IncompleteDownloadDir.Should().Be("/custom/incomplete");
        resource.EnableIncompleteDir.Should().BeTrue();
        resource.AutoRecheckOnCompletion.Should().BeTrue();
        resource.PreallocationMode.Should().Be("Full");
        resource.DiskPreAllocationMode.Should().Be("Full");
        resource.RenamePartialFiles.Should().BeTrue();
        resource.Umask.Should().Be("022");
        resource.LowDiskSpaceThresholdMb.Should().Be(1000);
        resource.MaxTorrentFileSizeBytes.Should().Be(100L * 1024 * 1024);

        resource.DownloadQueueSize.Should().Be(10);
        resource.SeedQueueSize.Should().Be(20);
        resource.QueueStalledEnabled.Should().BeTrue();
        resource.QueueStalledMinutes.Should().Be(15);
        resource.IdleSeedingLimitMinutes.Should().Be(60);
        resource.IncompleteExtension.Should().Be(".part");
        resource.GlobalShareLimitAction.Should().Be("Stop");
        resource.AutoShutdownAction.Should().Be("Hibernate");
        resource.AutoShutdownCondition.Should().Be("OnDownloadsComplete");

        resource.NetworkInterfaceBinding.Should().Be("eth0");
        resource.MaxConnectionsPerIp.Should().Be(8);
        resource.MaximumHalfOpenConnections.Should().Be(50);
        resource.AnonymousMode.Should().BeTrue();
        resource.ForceProxy.Should().BeTrue();
        resource.PeerDscp.Should().Be(4);
        resource.PeerPortRandomOnStart.Should().BeTrue();
        resource.PeerPortRandomLow.Should().Be(50000);
        resource.PeerPortRandomHigh.Should().Be(55000);

        resource.DiskCacheBytes.Should().Be(64L * 1024 * 1024);
        resource.DiskCachePolicy.Should().Be("Fifo");
        resource.FastResumeMode.Should().Be("Accurate");
        resource.AutoSaveFastResumeIntervalSeconds.Should().Be(300);
        resource.AutoSaveLoadMagnetMetadata.Should().BeTrue();
        resource.AutoSaveLoadDhtCache.Should().BeTrue();
        resource.PiecePickerStrategy.Should().Be("RarestFirst");
        resource.EndGamePickerEnabled.Should().BeTrue();
        resource.StaleRequestTimeoutSeconds.Should().Be(20);
        resource.WebSeedDelaySeconds.Should().Be(30);
        resource.MaximumDiskReadRateKbps.Should().Be(50000);
        resource.MaximumDiskWriteRateKbps.Should().Be(60000);

        resource.HashingThreads.Should().Be(4);
        resource.AioThreads.Should().Be(8);
        resource.DiskIoWriteMode.Should().Be("Asynchronous");
        resource.DiskIoReadMode.Should().Be("Asynchronous");
        resource.FilePoolSize.Should().Be(128);
        resource.ChokingAlgorithm.Should().Be("FixedSlots");
        resource.SeedChokingAlgorithm.Should().Be("RoundRobin");
        resource.MixedModeAlgorithm.Should().Be("PreferTcp");
        resource.AlertMask.Should().Be("All");

        resource.ScriptTorrentDoneFilename.Should().Be("/scripts/done.sh");
        resource.ScriptTorrentAddedFilename.Should().Be("/scripts/added.sh");
        resource.ScriptTorrentDoneSeedingFilename.Should().Be("/scripts/seeding_done.sh");
        resource.PrefetchEnabled.Should().BeTrue();
        resource.ScrapePausedTorrentsEnabled.Should().BeTrue();
        resource.RpcWhitelistEnabled.Should().BeTrue();
        resource.RpcWhitelist.Should().Be("127.0.0.1");

        resource.OnDownloadCompleteScript.Should().Be("/scripts/complete.sh");
        resource.OnSeedGoalReachedScript.Should().Be("/scripts/seed_goal.sh");
        resource.DefaultTrackers.Should().Be("udp://tracker.open.org:1337");
        resource.DhtBootstrapNodes.Should().Be("dht.transmissionbt.com:6881");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void BitTorrentConfigResourceMapper_WhenDownloadDirNullOrWhitespace_DefaultsToSlashDownloads(string emptyDir)
    {
        this.configService.DownloadDir.Returns(emptyDir);

        var resource = BitTorrentConfigResourceMapper.ToResource(this.configService);

        resource.DownloadDir.Should().Be("/downloads");
    }

    [Test]
    public async Task BitTorrentConfigController_GetConfig_And_SaveConfig_FlowsCorrectly()
    {
        this.configService.ActiveTorrentEngine.Returns("MonoTorrent");
        this.configService.AnnounceIntervalSeconds.Returns(1800);
        this.configService.MinAnnounceIntervalSeconds.Returns(300);
        this.configService.ScrapeIntervalSeconds.Returns(1200);

        var controller = new BitTorrentConfigController(this.configService);

        var config = controller.GetConfig();
        config.Should().NotBeNull();
        config.Id.Should().Be(1);
        config.ActiveTorrentEngine.Should().Be("MonoTorrent");

        var configById = controller.GetConfigById(1);
        configById.Should().NotBeNull();
        configById.Id.Should().Be(1);

        // Validation failure: AnnounceIntervalSeconds < 60
        var invalidResource = new BitTorrentConfigResource
        {
            AnnounceIntervalSeconds = 59,
            MinAnnounceIntervalSeconds = 30,
            ScrapeIntervalSeconds = 60,
        };
        var badResult = await controller.SaveConfig(invalidResource);
        badResult.Result.Should().BeOfType<BadRequestObjectResult>();

        // Valid resource save
        var validResource = new BitTorrentConfigResource
        {
            AnnounceIntervalSeconds = 120,
            MinAnnounceIntervalSeconds = 60,
            ScrapeIntervalSeconds = 120,
            DownloadDir = "/downloads",
        };
        var okResult = await controller.SaveConfig(validResource);
        okResult.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["AnnounceIntervalSeconds"] == 120 &&
            (int)d["MinAnnounceIntervalSeconds"] == 60));
    }

    // ---------------------------------------------------------
    // ProtocolsConfigResource & ProtocolsConfigController
    // ---------------------------------------------------------

    [Test]
    public void ProtocolsConfigResource_DefaultValues_AreCorrect()
    {
        var resource = new ProtocolsConfigResource();

        resource.EnableBep27PrivateTorrents.Should().BeTrue();
    }

    [Test]
    public void ProtocolsConfigResourceMapper_ToResource_MapsAllProperties()
    {
        this.configService.ExtensionUtMetadata.Returns(true);
        this.configService.ExtensionUtPex.Returns(true);
        this.configService.ExtensionLtDontHave.Returns(true);
        this.configService.ExtensionFastExtension.Returns(true);
        this.configService.EnableBep27PrivateTorrents.Returns(true);
        this.configService.UtpEnabled.Returns(true);
        this.configService.TcpFallback.Returns(true);
        this.configService.TransportConnectionTimeoutSeconds.Returns(15);
        this.configService.PexInterval.Returns(60);
        this.configService.PexMaxPeersPerMessage.Returns(50);
        this.configService.MultiTrackerEnabled.Returns(true);
        this.configService.MultiTrackerFailoverEnabled.Returns(true);
        this.configService.AnnounceToAllTiers.Returns(true);
        this.configService.AnnounceToAllInTier.Returns(true);
        this.configService.FailoverMaxConsecutiveFailures.Returns(5);
        this.configService.FailoverBackoffBaseSeconds.Returns(10);
        this.configService.FailoverMaxBackoffSeconds.Returns(300);
        this.configService.DhtRoutingTableSize.Returns(5000);
        this.configService.DhtAnnouncementInterval.Returns(1800);
        this.configService.DhtBootstrapTimeout.Returns(30);
        this.configService.DhtQueryTimeout.Returns(10);
        this.configService.DhtMaxNodes.Returns(8000);
        this.configService.DhtBucketSize.Returns(8);
        this.configService.DhtConcurrentQueries.Returns(5);
        this.configService.DhtAutoBootstrap.Returns(true);
        this.configService.DhtRateLimitEnabled.Returns(true);
        this.configService.DhtMaxQueriesPerSecond.Returns(100);

        var resource = ProtocolsConfigResourceMapper.ToResource(this.configService);

        resource.ExtensionUtMetadata.Should().BeTrue();
        resource.ExtensionUtPex.Should().BeTrue();
        resource.ExtensionLtDontHave.Should().BeTrue();
        resource.ExtensionFastExtension.Should().BeTrue();
        resource.EnableBep27PrivateTorrents.Should().BeTrue();
        resource.UtpEnabled.Should().BeTrue();
        resource.TcpFallback.Should().BeTrue();
        resource.TransportConnectionTimeoutSeconds.Should().Be(15);
        resource.PexInterval.Should().Be(60);
        resource.PexMaxPeersPerMessage.Should().Be(50);
        resource.MultiTrackerEnabled.Should().BeTrue();
        resource.MultiTrackerFailoverEnabled.Should().BeTrue();
        resource.AnnounceToAllTiers.Should().BeTrue();
        resource.AnnounceToAllInTier.Should().BeTrue();
        resource.FailoverMaxConsecutiveFailures.Should().Be(5);
        resource.FailoverBackoffBaseSeconds.Should().Be(10);
        resource.FailoverMaxBackoffSeconds.Should().Be(300);
        resource.DhtRoutingTableSize.Should().Be(5000);
        resource.DhtAnnouncementInterval.Should().Be(1800);
        resource.DhtBootstrapTimeout.Should().Be(30);
        resource.DhtQueryTimeout.Should().Be(10);
        resource.DhtMaxNodes.Should().Be(8000);
        resource.DhtBucketSize.Should().Be(8);
        resource.DhtConcurrentQueries.Should().Be(5);
        resource.DhtAutoBootstrap.Should().BeTrue();
        resource.DhtRateLimitEnabled.Should().BeTrue();
        resource.DhtMaxQueriesPerSecond.Should().Be(100);
    }

    [Test]
    public async Task ProtocolsConfigController_GetConfig_And_Validation_FlowsCorrectly()
    {
        this.configService.PexInterval.Returns(60);
        this.configService.TransportConnectionTimeoutSeconds.Returns(10);
        this.configService.PexMaxPeersPerMessage.Returns(20);
        this.configService.FailoverMaxConsecutiveFailures.Returns(3);
        this.configService.DhtBucketSize.Returns(8);
        this.configService.DhtMaxQueriesPerSecond.Returns(50);

        var controller = new ProtocolsConfigController(this.configService);

        var config = controller.GetConfig();
        config.Should().NotBeNull();
        config.Id.Should().Be(1);

        // Validation failure: PexInterval < 10
        var invalidResource = new ProtocolsConfigResource
        {
            TransportConnectionTimeoutSeconds = 5,
            PexInterval = 9,
            PexMaxPeersPerMessage = 10,
            FailoverMaxConsecutiveFailures = 2,
            DhtBucketSize = 8,
            DhtMaxQueriesPerSecond = 10,
        };
        var badResult = await controller.SaveConfig(invalidResource);
        badResult.Result.Should().BeOfType<BadRequestObjectResult>();

        // Valid resource save
        var validResource = new ProtocolsConfigResource
        {
            TransportConnectionTimeoutSeconds = 5,
            PexInterval = 15,
            PexMaxPeersPerMessage = 10,
            FailoverMaxConsecutiveFailures = 2,
            DhtBucketSize = 8,
            DhtMaxQueriesPerSecond = 10,
            UtpEnabled = true,
        };
        var okResult = await controller.SaveConfig(validResource);
        okResult.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["PexInterval"] == 15 &&
            (bool)d["UtpEnabled"] == true));
    }

    // ---------------------------------------------------------
    // NetworkConfigResource & NetworkConfigController
    // ---------------------------------------------------------

    [Test]
    public void NetworkConfigResource_DefaultValues_AreCorrect()
    {
        var resource = new NetworkConfigResource();

        resource.EnableIPv6.Should().BeTrue();
    }

    [Test]
    public void NetworkConfigResourceMapper_ToResource_MapsAllPropertiesAndMasksPassword()
    {
        this.configService.ListeningPort.Returns(6881);
        this.configService.UpnpEnabled.Returns(true);
        this.configService.EnableIPv6.Returns(true);
        this.configService.BindInterface.Returns("192.168.1.100");
        this.configService.EnableVpnKillSwitch.Returns(true);
        this.configService.MaxGlobalConnections.Returns(200);
        this.configService.MaxPerTorrentConnections.Returns(50);
        this.configService.MaxUploadSlots.Returns(10);
        this.configService.MaxConnectionsPerIp.Returns(4);
        this.configService.MaximumHalfOpenConnections.Returns(20);
        this.configService.AnonymousMode.Returns(false);
        this.configService.ForceProxy.Returns(true);
        this.configService.PeerDscp.Returns(8);
        this.configService.ProxyType.Returns("Socks5");
        this.configService.ProxyHost.Returns("proxy.vpn.com");
        this.configService.ProxyPort.Returns(1080);
        this.configService.ProxyAuthEnabled.Returns(true);
        this.configService.ProxyUsername.Returns("proxyuser");
        this.configService.ProxyPassword.Returns("supersecret");
        this.configService.ProxyBypassLocalNetworks.Returns(true);

        var resource = NetworkConfigResourceMapper.ToResource(this.configService);

        resource.ListeningPort.Should().Be(6881);
        resource.UpnpEnabled.Should().BeTrue();
        resource.EnableIPv6.Should().BeTrue();
        resource.BindInterface.Should().Be("192.168.1.100");
        resource.EnableVpnKillSwitch.Should().BeTrue();
        resource.MaxGlobalConnections.Should().Be(200);
        resource.MaxPerTorrentConnections.Should().Be(50);
        resource.MaxUploadSlots.Should().Be(10);
        resource.MaxConnectionsPerIp.Should().Be(4);
        resource.MaximumHalfOpenConnections.Should().Be(20);
        resource.AnonymousMode.Should().BeFalse();
        resource.ForceProxy.Should().BeTrue();
        resource.PeerDscp.Should().Be(8);
        resource.ProxyType.Should().Be("Socks5");
        resource.ProxyHost.Should().Be("proxy.vpn.com");
        resource.ProxyPort.Should().Be(1080);
        resource.ProxyAuthEnabled.Should().BeTrue();
        resource.ProxyUsername.Should().Be("proxyuser");
        resource.ProxyPassword.Should().Be("********");
        resource.ProxyBypassLocalNetworks.Should().BeTrue();

        // Empty password should map to empty string
        this.configService.ProxyPassword.Returns(string.Empty);
        var emptyPassResource = NetworkConfigResourceMapper.ToResource(this.configService);
        emptyPassResource.ProxyPassword.Should().Be(string.Empty);
    }

    [Test]
    public async Task NetworkConfigController_SaveConfig_WhenMaskedPassword_RestoresOriginalPassword()
    {
        this.configService.ProxyPassword.Returns("real_secret_vpn_pw");

        var controller = new NetworkConfigController(this.configService);

        var resource = new NetworkConfigResource
        {
            ListeningPort = 6881,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 10,
            ProxyPort = 1080,
            ProxyPassword = "********",
        };

        var result = await controller.SaveConfig(resource);

        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "real_secret_vpn_pw"));
    }

    [Test]
    public async Task NetworkConfigController_SaveConfig_WhenNewPassword_PreservesNewPassword()
    {
        this.configService.ProxyPassword.Returns("old_password");

        var controller = new NetworkConfigController(this.configService);

        var resource = new NetworkConfigResource
        {
            ListeningPort = 6881,
            MaxGlobalConnections = 200,
            MaxPerTorrentConnections = 50,
            MaxUploadSlots = 10,
            ProxyPort = 1080,
            ProxyPassword = "new_secret_password_123",
        };

        var result = await controller.SaveConfig(resource);

        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (string)d["ProxyPassword"] == "new_secret_password_123"));
    }

    [Test]
    public async Task NetworkConfigController_ValidationRules()
    {
        var controller = new NetworkConfigController(this.configService);

        // ListeningPort out of bounds
        var invalidPort = new NetworkConfigResource
        {
            ListeningPort = 0,
            MaxGlobalConnections = 100,
            MaxPerTorrentConnections = 20,
            MaxUploadSlots = 5,
            ProxyPort = 1080,
        };
        (await controller.SaveConfig(invalidPort)).Result.Should().BeOfType<BadRequestObjectResult>();

        // MaxGlobalConnections < 1
        var invalidConn = new NetworkConfigResource
        {
            ListeningPort = 6881,
            MaxGlobalConnections = 0,
            MaxPerTorrentConnections = 20,
            MaxUploadSlots = 5,
            ProxyPort = 1080,
        };
        (await controller.SaveConfig(invalidConn)).Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ---------------------------------------------------------
    // SeedingConfigResource & SeedingConfigController
    // ---------------------------------------------------------

    [Test]
    public void SeedingConfigResourceMapper_ToResource_MapsAllProperties()
    {
        this.configService.MaxUploadSpeedKbps.Returns(5000);
        this.configService.MaxDownloadSpeedKbps.Returns(10000);
        this.configService.AlternativeSpeedEnabled.Returns(true);
        this.configService.AltUploadSpeedKbps.Returns(1000);
        this.configService.AltDownloadSpeedKbps.Returns(2000);
        this.configService.GlobalSeedRatioLimit.Returns(2.5);
        this.configService.UploadDistributionAlgorithm.Returns("RoundRobin");
        this.configService.UploadDistributionSpreadPercentage.Returns(75);
        this.configService.UploadRedistributionMode.Returns("Proportional");
        this.configService.UploadCustomIntervalMinutes.Returns(10);
        this.configService.UploadStoppedMinPercentage.Returns(5);
        this.configService.UploadStoppedMaxPercentage.Returns(25);
        this.configService.DownloadDistributionAlgorithm.Returns("Adaptive");
        this.configService.DownloadDistributionSpreadPercentage.Returns(80);
        this.configService.DownloadRedistributionMode.Returns("FairShare");
        this.configService.DownloadCustomIntervalMinutes.Returns(15);
        this.configService.DownloadStoppedMinPercentage.Returns(10);
        this.configService.DownloadStoppedMaxPercentage.Returns(30);
        this.configService.SpeedVariationMin.Returns(0.8);
        this.configService.SpeedVariationMax.Returns(1.2);

        var resource = SeedingConfigResourceMapper.ToResource(this.configService);

        resource.MaxUploadSpeedKbps.Should().Be(5000);
        resource.MaxDownloadSpeedKbps.Should().Be(10000);
        resource.AlternativeSpeedEnabled.Should().BeTrue();
        resource.AltUploadSpeedKbps.Should().Be(1000);
        resource.AltDownloadSpeedKbps.Should().Be(2000);
        resource.GlobalSeedRatioLimit.Should().Be(2.5);
        resource.UploadDistributionAlgorithm.Should().Be("RoundRobin");
        resource.UploadDistributionSpreadPercentage.Should().Be(75);
        resource.UploadRedistributionMode.Should().Be("Proportional");
        resource.UploadCustomIntervalMinutes.Should().Be(10);
        resource.UploadStoppedMinPercentage.Should().Be(5);
        resource.UploadStoppedMaxPercentage.Should().Be(25);
        resource.DownloadDistributionAlgorithm.Should().Be("Adaptive");
        resource.DownloadDistributionSpreadPercentage.Should().Be(80);
        resource.DownloadRedistributionMode.Should().Be("FairShare");
        resource.DownloadCustomIntervalMinutes.Should().Be(15);
        resource.DownloadStoppedMinPercentage.Should().Be(10);
        resource.DownloadStoppedMaxPercentage.Should().Be(30);
        resource.SpeedVariationMin.Should().Be(0.8);
        resource.SpeedVariationMax.Should().Be(1.2);
    }

    [Test]
    public async Task SeedingConfigController_ValidationRules_And_SaveConfig()
    {
        var controller = new SeedingConfigController(this.configService);

        // Negative speed fails
        var negativeSpeed = new SeedingConfigResource
        {
            MaxUploadSpeedKbps = -1,
            MaxDownloadSpeedKbps = 100,
            AltUploadSpeedKbps = 50,
            AltDownloadSpeedKbps = 50,
            GlobalSeedRatioLimit = 1.0,
            UploadDistributionSpreadPercentage = 50,
            DownloadDistributionSpreadPercentage = 50,
        };
        (await controller.SaveConfig(negativeSpeed)).Result.Should().BeOfType<BadRequestObjectResult>();

        // Spread percentage > 100 fails
        var invalidSpread = new SeedingConfigResource
        {
            MaxUploadSpeedKbps = 100,
            MaxDownloadSpeedKbps = 100,
            AltUploadSpeedKbps = 50,
            AltDownloadSpeedKbps = 50,
            GlobalSeedRatioLimit = 1.0,
            UploadDistributionSpreadPercentage = 101,
            DownloadDistributionSpreadPercentage = 50,
        };
        (await controller.SaveConfig(invalidSpread)).Result.Should().BeOfType<BadRequestObjectResult>();

        // Valid resource succeeds
        var valid = new SeedingConfigResource
        {
            MaxUploadSpeedKbps = 1000,
            MaxDownloadSpeedKbps = 5000,
            AltUploadSpeedKbps = 200,
            AltDownloadSpeedKbps = 500,
            GlobalSeedRatioLimit = 2.0,
            UploadDistributionSpreadPercentage = 50,
            DownloadDistributionSpreadPercentage = 60,
        };
        var result = await controller.SaveConfig(valid);
        result.Result.Should().BeOfType<AcceptedResult>();
        this.configService.Received(1).SaveConfigDictionary(Arg.Is<Dictionary<string, object>>(d =>
            (int)d["MaxUploadSpeedKbps"] == 1000 &&
            (double)d["GlobalSeedRatioLimit"] == 2.0));
    }

    // ---------------------------------------------------------
    // General ConfigController<T> Error Handling
    // ---------------------------------------------------------

    [Test]
    public async Task ConfigController_SaveConfig_WhenNull_ReturnsBadRequest()
    {
        var controller = new SeedingConfigController(this.configService);

        var result = await controller.SaveConfig(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result.Result!;
        bad.Value.Should().Be("Request body cannot be empty.");
    }

    [Test]
    public async Task ConfigController_SaveConfig_WhenConfigServiceThrows_Returns500InternalServerError()
    {
        var controller = new SeedingConfigController(this.configService);

        this.configService.When(c => c.SaveConfigDictionary(Arg.Any<Dictionary<string, object>>()))
            .Do(_ => throw new InvalidOperationException("Failed to write config"));

        var valid = new SeedingConfigResource
        {
            MaxUploadSpeedKbps = 100,
            MaxDownloadSpeedKbps = 100,
            AltUploadSpeedKbps = 50,
            AltDownloadSpeedKbps = 50,
            GlobalSeedRatioLimit = 1.0,
            UploadDistributionSpreadPercentage = 50,
            DownloadDistributionSpreadPercentage = 50,
        };

        var result = await controller.SaveConfig(valid);

        result.Result.Should().BeOfType<ObjectResult>();
        var serverError = (ObjectResult)result.Result!;
        serverError.StatusCode.Should().Be(500);
        serverError.Value.Should().Be("Failed to save configuration.");
    }
}
