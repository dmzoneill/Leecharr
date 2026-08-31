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
    private IBasicRepository<ConfigModel> _repository = null!;
    private IEventAggregator _eventAggregator = null!;
    private Logger _logger = null!;
    private List<ConfigModel> _store = null!;
    private ConfigService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new List<ConfigModel>();
        _repository = Substitute.For<IBasicRepository<ConfigModel>>();
        _repository.All().Returns(_ => _store.ToList());
        _repository.Insert(Arg.Any<ConfigModel>()).Returns(call =>
        {
            var model = call.Arg<ConfigModel>();
            model.Id = _store.Count + 1;
            _store.Add(model);
            return model;
        });
        _repository.Update(Arg.Any<ConfigModel>()).Returns(call =>
        {
            var model = call.Arg<ConfigModel>();
            var idx = _store.FindIndex(m => m.Key.Equals(model.Key, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _store[idx] = model;
            }

            return model;
        });

        _eventAggregator = Substitute.For<IEventAggregator>();
        _logger = LogManager.GetCurrentClassLogger();
        _service = new ConfigService(_repository, _eventAggregator, _logger);
    }

    [Test]
    public void Defaults_AreReturned_WhenStoreIsEmpty()
    {
        _service.ActiveTorrentEngine.Should().Be("MonoTorrent");
        _service.ActiveArchiveExtractor.Should().Be("SharpCompress");
        _service.ActiveMediaInspector.Should().Be("TagLib");
        _service.DiskWriteCacheSizeMb.Should().Be(128);
        _service.ListeningPort.Should().Be(51413);
        _service.EnableDht.Should().BeTrue();
        _service.EnablePex.Should().BeTrue();
        _service.EnableLpd.Should().BeTrue();
        _service.MaxGlobalConnections.Should().Be(300);
        _service.MaxActiveDownloads.Should().Be(3);
        _service.ThemeStyle.Should().Be("dark");
        _service.AutoEnrichEnabled.Should().BeTrue();
        _service.SchedulerMonday.Should().BeTrue();
        _service.GlobalSeedRatioLimit.Should().Be(0.0);
    }

    [Test]
    public void SaveConfigDictionary_InsertsNewValuesAndPublishesEvent()
    {
        var values = new Dictionary<string, object>
        {
            { "DownloadDir", "/media/downloads" },
            { "ListeningPort", 55000 },
            { "EnableDht", false },
            { "GlobalSeedRatioLimit", 2.5 }
        };

        _service.SaveConfigDictionary(values);

        _service.DownloadDir.Should().Be("/media/downloads");
        _service.ListeningPort.Should().Be(55000);
        _service.EnableDht.Should().BeFalse();
        _service.GlobalSeedRatioLimit.Should().Be(2.5);

        _eventAggregator.Received(1).PublishEvent(Arg.Any<ConfigSavedEvent>());
    }

    [Test]
    public void SaveConfigDictionary_UpdatesExistingValues()
    {
        _store.Add(new ConfigModel { Id = 1, Key = "DownloadDir", Value = "/old/path" });

        var values = new Dictionary<string, object>
        {
            { "DownloadDir", "/new/path" }
        };

        _service.SaveConfigDictionary(values);

        _service.DownloadDir.Should().Be("/new/path");
        _repository.Received(1).Update(Arg.Is<ConfigModel>(c => c.Key == "DownloadDir" && c.Value == "/new/path"));
    }

    [Test]
    public void GetValueTypes_HandlesParsingAndFallbacks()
    {
        _store.Add(new ConfigModel { Key = "TestBoolTrue", Value = "true" });
        _store.Add(new ConfigModel { Key = "TestBoolFalse", Value = "false" });
        _store.Add(new ConfigModel { Key = "TestBoolInvalid", Value = "notabool" });
        _store.Add(new ConfigModel { Key = "TestInt", Value = "42" });
        _store.Add(new ConfigModel { Key = "TestIntInvalid", Value = "notanint" });
        _store.Add(new ConfigModel { Key = "TestDouble", Value = "3.14159" });
        _store.Add(new ConfigModel { Key = "TestDoubleInvalid", Value = "notadouble" });

        _service.GetValueBoolean("TestBoolTrue", false).Should().BeTrue();
        _service.GetValueBoolean("TestBoolFalse", true).Should().BeFalse();
        _service.GetValueBoolean("TestBoolInvalid", true).Should().BeTrue();
        _service.GetValueBoolean("NonExistent", false).Should().BeFalse();

        _service.GetValueInt("TestInt", 0).Should().Be(42);
        _service.GetValueInt("TestIntInvalid", 99).Should().Be(99);
        _service.GetValueInt("NonExistent", 100).Should().Be(100);

        _service.GetValueDouble("TestDouble", 0.0).Should().BeApproximately(3.14159, 0.0001);
        _service.GetValueDouble("TestDoubleInvalid", 1.5).Should().Be(1.5);
        _service.GetValueDouble("NonExistent", 2.0).Should().Be(2.0);
    }

    [Test]
    public void InstanceUuid_GeneratesAndPersists_WhenMissing()
    {
        var uuid = _service.InstanceUuid;

        uuid.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(uuid, out _).Should().BeTrue();

        // Calling again returns the same persisted UUID
        var uuid2 = _service.InstanceUuid;
        uuid2.Should().Be(uuid);
    }
}
