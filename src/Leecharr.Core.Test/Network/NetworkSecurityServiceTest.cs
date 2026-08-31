using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkSecurityServiceTest
{
    private INetworkSettingsRepository _repository = null!;
    private IEventAggregator _eventAggregator = null!;
    private NetworkSecurityService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<INetworkSettingsRepository>();
        _eventAggregator = Substitute.For<IEventAggregator>();
        _service = new NetworkSecurityService(_repository, _eventAggregator);
    }

    [Test]
    public void IsInterfaceActive_WhenInterfaceEmpty_ReturnsTrue()
    {
        var result = _service.IsInterfaceActive(string.Empty);
        result.Should().BeTrue();
    }

    [Test]
    public void CheckVpnKillSwitch_WhenDisabled_ReturnsFalse()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = false,
            BindInterface = "tun0"
        };
        _repository.GetSettings().Returns(settings);

        var triggered = _service.CheckVpnKillSwitch();

        triggered.Should().BeFalse();
        _eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnKillSwitchTriggeredEvent>());
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceDropped_TriggersKillSwitchAndPublishesEvent()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = true,
            BindInterface = "nonexistent_tun0"
        };
        _repository.GetSettings().Returns(settings);

        var triggered = _service.CheckVpnKillSwitch();

        triggered.Should().BeTrue();
        _eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "nonexistent_tun0"));
    }

    [Test]
    public void SaveSettings_WhenNew_CallsInsert()
    {
        var settings = new NetworkSettings { Id = 0, BindInterface = "tun0" };

        _service.SaveSettings(settings);

        _repository.Received(1).Insert(settings);
    }

    [Test]
    public void SaveSettings_WhenExisting_CallsUpdate()
    {
        var settings = new NetworkSettings { Id = 1, BindInterface = "wg0" };

        _service.SaveSettings(settings);

        _repository.Received(1).Update(settings);
    }
}
