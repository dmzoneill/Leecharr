// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Instrumentation;

namespace Leecharr.Core.Test.HealthCheck;

[TestFixture]
public class FileDescriptorExhaustionCheckTest
{
    private IFileDescriptorProvider provider;
    private FileDescriptorExhaustionCheck subject;

    [SetUp]
    public void SetUp()
    {
        this.provider = Substitute.For<IFileDescriptorProvider>();
        this.subject = new FileDescriptorExhaustionCheck(this.provider);
    }

    [Test]
    public async Task CheckAsync_ReturnsOk_WhenSoftLimitIsUnlimited()
    {
        this.provider.GetFileDescriptorUsagePercentage().Returns((double?)null);
        this.provider.GetMaxFileDescriptors().Returns(-1);
        this.provider.GetOpenFileDescriptorCount().Returns(120);

        var result = await this.subject.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public async Task CheckAsync_ReturnsWarning_WhenUsageCannotBeDetermined()
    {
        this.provider.GetFileDescriptorUsagePercentage().Returns((double?)null);
        this.provider.GetMaxFileDescriptors().Returns(0);
        this.provider.GetOpenFileDescriptorCount().Returns(-1);

        var result = await this.subject.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Source, Is.EqualTo("FileDescriptors"));
        Assert.That(result.Message, Does.Contain("Unable to determine file descriptor usage"));
        Assert.That(result.Message, Does.Contain("-1/0"));
    }

    [Test]
    public async Task CheckAsync_ReturnsOk_WhenUsageBelow80Percent()
    {
        this.provider.GetFileDescriptorUsagePercentage().Returns(79.9);
        this.provider.GetOpenFileDescriptorCount().Returns(818);
        this.provider.GetMaxFileDescriptors().Returns(1024);

        var result = await this.subject.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Ok));
    }

    [Test]
    public async Task CheckAsync_ReturnsWarning_WhenUsageBetween80And90Percent()
    {
        this.provider.GetFileDescriptorUsagePercentage().Returns(82.5);
        this.provider.GetOpenFileDescriptorCount().Returns(845);
        this.provider.GetMaxFileDescriptors().Returns(1024);

        var result = await this.subject.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Warning));
        Assert.That(result.Message, Does.Contain("82.5%"));
        Assert.That(result.Message, Does.Contain("845/1024"));
    }

    [Test]
    public async Task CheckAsync_ReturnsError_WhenUsageAtOrAbove90Percent()
    {
        this.provider.GetFileDescriptorUsagePercentage().Returns(92.0);
        this.provider.GetOpenFileDescriptorCount().Returns(942);
        this.provider.GetMaxFileDescriptors().Returns(1024);

        var result = await this.subject.CheckAsync();

        Assert.That(result.Type, Is.EqualTo(HealthCheckResultType.Error));
        Assert.That(result.Message, Does.Contain("92%"));
        Assert.That(result.Message, Does.Contain("EMFILE"));
    }
}
