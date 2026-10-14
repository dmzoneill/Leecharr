// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Packages;

namespace Leecharr.Core.Test.Packages;

[TestFixture]
public class PackagePathTranslatorTest
{
    [Test]
    public void IsStrictlyInsideDirectory_AcceptsAChildAndRejectsTheRootAndSiblings()
    {
        var root = Path.Combine(Path.GetTempPath(), "leecharr-path-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var child = Path.Combine(root, "import");
            PackagePathTranslator.IsStrictlyInsideDirectory(child, root).Should().BeTrue();
            PackagePathTranslator.IsStrictlyInsideDirectory(root, root).Should().BeFalse();
            PackagePathTranslator.IsStrictlyInsideDirectory(root + "-other", root).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root);
        }
    }
}
