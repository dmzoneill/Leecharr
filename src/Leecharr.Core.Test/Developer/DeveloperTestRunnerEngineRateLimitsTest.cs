// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Developer.Testing;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperTestRunnerEngineRateLimitsTest
{
    [Test]
    public async Task RunTestAsync_EngineRateLimits_DoesNotMutateLiveEngineCaps()
    {
        var downloadEngine = Substitute.For<IDownloadEngine, ITorrentEngine>();
        var torrentEngine = (ITorrentEngine)downloadEngine;
        torrentEngine.Capabilities.Returns(new TorrentEngineCapabilities { SupportsDynamicRateLimits = true });

        var configService = Substitute.For<IConfigService>();
        configService.MaxDownloadSpeedKbps.Returns(5000);
        configService.MaxUploadSpeedKbps.Returns(2000);

        var runner = new DeveloperTestRunner(downloadEngine: downloadEngine, configService: configService);

        var result = await runner.RunTestAsync("engine-ratelimits");

        result.Status.Should().Be("Passed");
        result.Output.Should().Contain("DynamicRateLimits=True");
        result.Output.Should().Contain("download 5000 KB/s");
        result.Output.Should().Contain("upload 2000 KB/s");
        result.Output.Should().Contain("does not modify live engine caps");

        await downloadEngine.DidNotReceive().SetRateLimitsAsync(Arg.Any<int>(), Arg.Any<int>());
    }
}
