// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Update;

namespace Leecharr.Core.Test.Update;

[TestFixture]
public class UpdateInstallServiceTest
{
    [Test]
    public void BuildGitHubAssetDownloadUrl_EncodesFileName()
    {
        var url = UpdateCheckService.BuildGitHubAssetDownloadUrl("v1.0.0", "Leecharr.1.0.0.linux-x64.tar.gz");
        url.Should().Be("https://github.com/dmzoneill/Leecharr/releases/download/v1.0.0/Leecharr.1.0.0.linux-x64.tar.gz");
    }

    [Test]
    public void ResolvePayloadRoot_WhenSingleChildDirectory_UsesChild()
    {
        var root = Path.Combine(Path.GetTempPath(), "leecharr-update-test-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "Leecharr");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "Leecharr.dll"), "test");

        try
        {
            var diskProvider = new DiskProvider();
            UpdateInstallService.ResolvePayloadRoot(root, diskProvider).Should().Be(child);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ApplyPendingUpdate_CopiesStagedFilesToStartupFolder()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "leecharr-update-apply-" + Guid.NewGuid().ToString("N"));
        var appData = Path.Combine(tempRoot, "appdata");
        var startup = Path.Combine(tempRoot, "startup");
        var staging = Path.Combine(appData, "Update", "staging", "9.9.9", "Leecharr");
        Directory.CreateDirectory(staging);
        Directory.CreateDirectory(startup);
        var markerName = "update-marker.txt";
        var markerContents = "applied";
        File.WriteAllText(Path.Combine(staging, markerName), markerContents);

        var appFolderInfo = Substitute.For<IAppFolderInfo>();
        appFolderInfo.AppDataFolder.Returns(appData);
        appFolderInfo.StartUpFolder.Returns(startup);

        var manifest = new PendingUpdateManifest
        {
            Version = "9.9.9",
            PayloadRoot = staging,
            TargetFolder = startup,
        };
        var manifestPath = UpdateInstallService.GetPendingManifestPath(appData);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        try
        {
            var service = new UpdateInstallService(appFolderInfo);
            service.ApplyPendingUpdate().Should().BeTrue();
            File.ReadAllText(Path.Combine(startup, markerName)).Should().Be(markerContents);
            File.Exists(manifestPath).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
