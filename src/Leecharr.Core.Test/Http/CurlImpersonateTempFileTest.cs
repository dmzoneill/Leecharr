// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Http.Transport;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class CurlImpersonateTempFileTest
{
    [Test]
    public void CreatePrivateTempFile_CreatesAnEmptyFileWithAnUnpredictableName()
    {
        var path = CurlImpersonateTransportProvider.CreatePrivateTempFile();
        try
        {
            var expectedDirectory = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "leecharr",
                "curl"));
            Path.GetDirectoryName(Path.GetFullPath(path)).Should().Be(expectedDirectory);
            Path.GetFileName(path).Should().NotBeNullOrEmpty();
            File.Exists(path).Should().BeTrue();
            new FileInfo(path).Length.Should().Be(0);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
