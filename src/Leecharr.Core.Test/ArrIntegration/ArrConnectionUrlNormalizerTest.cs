// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrConnectionUrlNormalizerTest
{
    [TestCase("http://sonarr:8989/api/v3", "http://sonarr:8989")]
    [TestCase("http://sonarr:8989/api/v3/", "http://sonarr:8989")]
    [TestCase("http://lidarr:8686/api/v1", "http://lidarr:8686")]
    [TestCase("http://prowlarr:9696/api", "http://prowlarr:9696")]
    [TestCase("http://sonarr:8989", "http://sonarr:8989")]
    public void StripApiSuffix_RemovesTrailingApiSegments(string input, string expected)
    {
        ArrConnectionUrlNormalizer.StripApiSuffix(input).Should().Be(expected);
    }
}
