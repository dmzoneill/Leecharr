// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Config;
using NUnit.Framework;

namespace Leecharr.Core.Test.Config;

[TestFixture]
public class ConfigResourceDictionaryBuilderTest
{
    [Test]
    public void ToDictionary_WhenOnlyUiRefreshRateSupplied_SavesSingleKey()
    {
        var resource = new AdvancedConfigResource
        {
            UiRefreshRateSec = 42,
            SuppliedJsonPropertyNames = new HashSet<string>(new[] { "id", "uiRefreshRateSec" }, System.StringComparer.OrdinalIgnoreCase),
        };

        var dictionary = ConfigResourceDictionaryBuilder.ToDictionary(resource);

        dictionary.Should().ContainSingle();
        dictionary.Should().ContainKey("UiRefreshRateSec").WhoseValue.Should().Be(42);
    }

    [Test]
    public void ToDictionary_WhenSuppliedNamesMissing_SavesAllProperties()
    {
        var resource = new AdvancedConfigResource
        {
            LogToFile = true,
            UiRefreshRateSec = 5,
        };

        var dictionary = ConfigResourceDictionaryBuilder.ToDictionary(resource);

        dictionary.Should().ContainKey("LogToFile");
        dictionary.Should().ContainKey("UiRefreshRateSec");
    }
}
