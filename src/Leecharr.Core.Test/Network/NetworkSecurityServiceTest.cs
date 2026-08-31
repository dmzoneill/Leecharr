// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Network;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class NetworkSecurityServiceTest
{
    private INetworkSettingsRepository repository = null!;
    private IEventAggregator eventAggregator = null!;
    private NetworkSecurityService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<INetworkSettingsRepository>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.service = new NetworkSecurityService(this.repository, this.eventAggregator);
    }

    [Test]
    public void IsInterfaceActive_WhenInterfaceEmpty_ReturnsTrue()
    {
        var result = this.service.IsInterfaceActive(string.Empty);
        result.Should().BeTrue();
    }

    [Test]
    public void CheckVpnKillSwitch_WhenDisabled_ReturnsFalse()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = false,
            BindInterface = "tun0",
        };
        this.repository.GetSettings().Returns(settings);

        var triggered = this.service.CheckVpnKillSwitch();

        triggered.Should().BeFalse();
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<VpnKillSwitchTriggeredEvent>());
    }

    [Test]
    public void CheckVpnKillSwitch_WhenInterfaceDropped_TriggersKillSwitchAndPublishesEvent()
    {
        var settings = new NetworkSettings
        {
            EnableVpnKillSwitch = true,
            BindInterface = "nonexistent_tun0",
        };
        this.repository.GetSettings().Returns(settings);

        var triggered = this.service.CheckVpnKillSwitch();

        triggered.Should().BeTrue();
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<VpnKillSwitchTriggeredEvent>(e => e.InterfaceName == "nonexistent_tun0"));
    }

    [Test]
    public void SaveSettings_WhenNew_CallsInsert()
    {
        var settings = new NetworkSettings { Id = 0, BindInterface = "tun0" };

        this.service.SaveSettings(settings);

        this.repository.Received(1).Insert(settings);
    }

    [Test]
    public void SaveSettings_WhenExisting_CallsUpdate()
    {
        var settings = new NetworkSettings { Id = 1, BindInterface = "wg0" };

        this.service.SaveSettings(settings);

        this.repository.Received(1).Update(settings);
    }
}
