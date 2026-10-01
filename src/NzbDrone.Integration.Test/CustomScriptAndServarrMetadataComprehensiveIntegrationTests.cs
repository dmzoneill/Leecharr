// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CustomScriptAndServarrMetadataComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void CustomScriptService_TokenizeArguments_TokenizesAccurately()
    {
        // 1. Standard space-separated args
        var args1 = CustomScriptService.TokenizeArguments("arg1 arg2 arg3");
        args1.Should().Equal("arg1", "arg2", "arg3");

        // 2. Quoted args with spaces
        var args2 = CustomScriptService.TokenizeArguments("--name \"The Matrix (1999)\" --category 'Sci Fi'");
        args2.Should().Equal("--name", "The Matrix (1999)", "--category", "Sci Fi");

        // 3. Escaped quotes and special characters
        var args3 = CustomScriptService.TokenizeArguments("path=\"C:\\\\Program Files\\\\App\" --flag");
        args3.Should().HaveCount(2);

        // 4. Empty and whitespace-only
        CustomScriptService.TokenizeArguments("").Should().BeEmpty();
        CustomScriptService.TokenizeArguments("   ").Should().BeEmpty();
    }

    [Test]
    public async Task ServarrSyncMetadataProvider_CapabilitiesAndHealth_Succeeds()
    {
        var provider = new ServarrSyncMetadataProvider();

        provider.ProviderId.Should().Be("ServarrSync");
        provider.DisplayName.Should().NotBeNullOrWhiteSpace();
        provider.IsAvailable.Should().BeTrue();
        provider.Capabilities.SupportsMovies.Should().BeTrue();
        provider.Capabilities.SupportsTvSeries.Should().BeTrue();
        provider.Capabilities.SupportsMusic.Should().BeTrue();

        var health = await provider.ProbeHealthAsync();
        health.IsHealthy.Should().BeTrue();
    }
}
