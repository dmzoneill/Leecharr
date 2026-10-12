// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Host;

namespace Leecharr.Core.Test.Host;

[TestFixture]
public class IndexHtmlInjectionTest
{
    [Test]
    public void BuildHeadInjection_EscapesHtmlAndJson_ForMaliciousUrlBase()
    {
        var malicious = "/\"></script><script>alert(1)</script>";
        var injection = IndexHtmlInjection.BuildHeadInjection(malicious);

        injection.Should().NotContain("<script>alert(1)</script>");
        injection.Should().Contain("&lt;script&gt;alert(1)&lt;/script&gt;");
        injection.Should().Contain("urlBase: \"/\\\"&gt;&lt;/script&gt;&lt;script&gt;alert(1)&lt;/script&gt;\"");
    }

    [TestCase("", "/")]
    [TestCase("/leecharr", "/leecharr/")]
    public void BuildHeadInjection_UsesExpectedBaseHref(string urlBase, string expectedBaseHref)
    {
        var injection = IndexHtmlInjection.BuildHeadInjection(urlBase);
        injection.Should().StartWith($"<base href=\"{expectedBaseHref}\" />");
    }

    [Test]
    public void InjectIntoHtml_PrependsToHead()
    {
        var html = "<html><head><title>x</title></head></html>";
        var result = IndexHtmlInjection.InjectIntoHtml(html, "/app");

        result.Should().Contain("<head><base href=\"/app/\" />");
        result.Should().Contain("urlBase: \"/app\"");
    }
}
