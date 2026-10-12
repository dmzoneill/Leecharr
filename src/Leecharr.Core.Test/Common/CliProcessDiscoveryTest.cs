// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Common;

namespace Leecharr.Core.Test.Common;

[TestFixture]
public class CliProcessDiscoveryTest
{
    [Test]
    public void FindExecutable_ReturnsExplicitFilePathInAdditionalPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr-cli-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var binaryName = "leecharr-cli-discovery-bin";
        var explicitBinary = Path.Combine(tempDir, binaryName);

        try
        {
            File.WriteAllText(explicitBinary, string.Empty);

            var result = CliProcessDiscovery.FindExecutable(binaryName, null, [explicitBinary]);

            result.Should().Be(explicitBinary);
        }
        finally
        {
            if (File.Exists(explicitBinary))
            {
                File.Delete(explicitBinary);
            }

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir);
            }
        }
    }

    [Test]
    public void FindExecutable_StillResolvesBinaryInsideAdditionalDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "leecharr-cli-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var binaryName = "leecharr-cli-discovery-tool";
        var binaryPath = Path.Combine(tempDir, binaryName);

        try
        {
            File.WriteAllText(binaryPath, string.Empty);

            var result = CliProcessDiscovery.FindExecutable(binaryName, null, [tempDir]);

            result.Should().Be(binaryPath);
        }
        finally
        {
            if (File.Exists(binaryPath))
            {
                File.Delete(binaryPath);
            }

            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir);
            }
        }
    }
}
