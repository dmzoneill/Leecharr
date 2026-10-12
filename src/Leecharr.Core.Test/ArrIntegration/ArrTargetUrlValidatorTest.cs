// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrTargetUrlValidatorTest
{
    [Test]
    public void IsValidTargetUrl_WhenHostDoesNotResolve_ReturnsFalse()
    {
        var valid = ArrTargetUrlValidator.IsValidTargetUrl("http://leecharr-ssrf-test.invalid", out var error);

        valid.Should().BeFalse();
        error.Should().Contain("could not be resolved");
    }

    [TestCase("http://169.254.169.254/latest/meta-data")]
    [TestCase("http://metadata.google.internal/computeMetadata/v1")]
    public void IsValidTargetUrl_WhenProhibitedTarget_ReturnsFalse(string url)
    {
        ArrTargetUrlValidator.IsValidTargetUrl(url, out _).Should().BeFalse();
    }
}
