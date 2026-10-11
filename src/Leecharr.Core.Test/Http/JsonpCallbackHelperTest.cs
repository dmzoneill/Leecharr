// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Http.Security;
using NUnit.Framework;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class JsonpCallbackHelperTest
{
    [TestCase("myJsonpCallback", true)]
    [TestCase("_private", true)]
    [TestCase("$jquery", true)]
    [TestCase("foo.bar", true)]
    [TestCase("steal", true)]
    [TestCase("steal);extra(", false)]
    [TestCase("alert(1)", false)]
    [TestCase("", false)]
    [TestCase(".leadingDot", false)]
    [TestCase("trailingDot.", false)]
    [TestCase("1startsWithDigit", false)]
    public void IsValidCallbackName_ValidatesJavaScriptIdentifierSegments(string callback, bool expected)
    {
        JsonpCallbackHelper.IsValidCallbackName(callback).Should().Be(expected);
    }
}
