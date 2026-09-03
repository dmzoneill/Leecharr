// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Configuration;

[TestFixture]
public class ConfigServiceTest
{
    private IBasicRepository<ConfigModel> repository = null!;
    private IEventAggregator eventAggregator = null!;
    private Logger logger = null!;
    private List<ConfigModel> store = null!;
    private ConfigService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.store = new List<ConfigModel>();
        this.repository = Substitute.For<IBasicRepository<ConfigModel>>();
        this.repository.All().Returns(_ => this.store.ToList());
        this.repository.Insert(Arg.Any<ConfigModel>()).Returns(call =>
        {
            var model = call.Arg<ConfigModel>();
            model.Id = this.store.Count + 1;
            this.store.Add(model);
            return model;
        });
        this.repository.Update(Arg.Any<ConfigModel>()).Returns(call =>
        {
            var model = call.Arg<ConfigModel>();
            var idx = this.store.FindIndex(m => m.Key.Equals(model.Key, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                this.store[idx] = model;
            }

            return model;
        });

        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.logger = LogManager.GetCurrentClassLogger();
        this.service = new ConfigService(this.repository, this.eventAggregator, this.logger);
    }

    [Test]
    public void Defaults_AreReturned_WhenStoreIsEmpty()
    {
        this.service.ActiveTorrentEngine.Should().Be("MonoTorrent");
        this.service.ActiveArchiveExtractor.Should().Be("SharpCompress");
        this.service.ActiveMediaInspector.Should().Be("TagLib");
        this.service.DiskWriteCacheSizeMb.Should().Be(128);
        this.service.ListeningPort.Should().Be(51413);
        this.service.EnableDht.Should().BeTrue();
        this.service.EnablePex.Should().BeTrue();
        this.service.EnableLpd.Should().BeTrue();
        this.service.EnableBep27PrivateTorrents.Should().BeTrue();
        this.service.MaxGlobalConnections.Should().Be(300);
        this.service.MaxActiveDownloads.Should().Be(3);
        this.service.ThemeStyle.Should().Be("dark");
        this.service.AutoEnrichEnabled.Should().BeTrue();
        this.service.SchedulerMonday.Should().BeTrue();
        this.service.GlobalSeedRatioLimit.Should().Be(0.0);
    }

    [Test]
    public void SaveConfigDictionary_InsertsNewValuesAndPublishesEvent()
    {
        var values = new Dictionary<string, object>
        {
            { "DownloadDir", "/media/downloads" },
            { "ListeningPort", 55000 },
            { "EnableDht", false },
            { "EnableBep27PrivateTorrents", false },
            { "GlobalSeedRatioLimit", 2.5 },
        };

        this.service.SaveConfigDictionary(values);

        this.service.DownloadDir.Should().Be("/media/downloads");
        this.service.ListeningPort.Should().Be(55000);
        this.service.EnableDht.Should().BeFalse();
        this.service.EnableBep27PrivateTorrents.Should().BeFalse();
        this.service.GlobalSeedRatioLimit.Should().Be(2.5);

        this.eventAggregator.Received(1).PublishEvent(Arg.Any<ConfigSavedEvent>());
    }

    [Test]
    public void SaveConfigDictionary_UpdatesExistingValues()
    {
        this.store.Add(new ConfigModel { Id = 1, Key = "DownloadDir", Value = "/old/path" });

        var values = new Dictionary<string, object>
        {
            { "DownloadDir", "/new/path" },
        };

        this.service.SaveConfigDictionary(values);

        this.service.DownloadDir.Should().Be("/new/path");
        this.repository.Received(1).Update(Arg.Is<ConfigModel>(c => c.Key == "DownloadDir" && c.Value == "/new/path"));
    }

    [Test]
    public void GetValueTypes_HandlesParsingAndFallbacks()
    {
        this.store.Add(new ConfigModel { Key = "TestBoolTrue", Value = "true" });
        this.store.Add(new ConfigModel { Key = "TestBoolFalse", Value = "false" });
        this.store.Add(new ConfigModel { Key = "TestBoolInvalid", Value = "notabool" });
        this.store.Add(new ConfigModel { Key = "TestInt", Value = "42" });
        this.store.Add(new ConfigModel { Key = "TestIntInvalid", Value = "notanint" });
        this.store.Add(new ConfigModel { Key = "TestDouble", Value = "3.14159" });
        this.store.Add(new ConfigModel { Key = "TestDoubleInvalid", Value = "notadouble" });

        this.service.GetValueBoolean("TestBoolTrue", false).Should().BeTrue();
        this.service.GetValueBoolean("TestBoolFalse", true).Should().BeFalse();
        this.service.GetValueBoolean("TestBoolInvalid", true).Should().BeTrue();
        this.service.GetValueBoolean("NonExistent", false).Should().BeFalse();

        this.service.GetValueInt("TestInt", 0).Should().Be(42);
        this.service.GetValueInt("TestIntInvalid", 99).Should().Be(99);
        this.service.GetValueInt("NonExistent", 100).Should().Be(100);

        this.service.GetValueDouble("TestDouble", 0.0).Should().BeApproximately(3.14159, 0.0001);
        this.service.GetValueDouble("TestDoubleInvalid", 1.5).Should().Be(1.5);
        this.service.GetValueDouble("NonExistent", 2.0).Should().Be(2.0);
    }

    [Test]
    public void InstanceUuid_GeneratesAndPersists_WhenMissing()
    {
        var uuid = this.service.InstanceUuid;

        uuid.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(uuid, out _).Should().BeTrue();

        // Calling again returns the same persisted UUID
        var uuid2 = this.service.InstanceUuid;
        uuid2.Should().Be(uuid);
    }
}
