// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.SystemServices;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class PowerManagementServiceTest
{
    [Test]
    public async Task ExecutePowerActionAsync_WithNone_ReturnsTrueWithoutWork()
    {
        var service = new PowerManagementService();
        var result = await service.ExecutePowerActionAsync(PowerAction.None);
        result.Should().BeTrue();
    }

    [Test]
    public void IsInContainer_DoesNotThrow()
    {
        var service = new PowerManagementService();
        _ = service.IsInContainer;
    }

    [Test]
    public async Task ExecutePowerActionAsync_WithExitApplicationAndHostLifetime_CallsStopApplication()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var service = new PowerManagementService(lifetime);

        var result = await service.ExecutePowerActionAsync(PowerAction.ExitApplication);

        result.Should().BeTrue();
        lifetime.Received(1).StopApplication();
    }
}
