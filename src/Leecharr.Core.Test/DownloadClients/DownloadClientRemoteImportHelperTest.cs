// Copyright (c) FeedItOut. All rights reserved.

using FluentAssertions;
using Leecharr.Api.V1.DownloadClients;
using NUnit.Framework;

namespace Leecharr.Core.Test.DownloadClients;

[TestFixture]
public class DownloadClientRemoteImportHelperTest
{
    [TestCase("pausedDL", true)]
    [TestCase("pausedUP", true)]
    [TestCase("queuedDL", true)]
    [TestCase("Queued", true)]
    [TestCase("Paused", true)]
    [TestCase("downloading", false)]
    [TestCase("uploading", false)]
    [TestCase("active", false)]
    public void ShouldStartPaused_MapsRemoteStates(string state, bool expected)
    {
        DownloadClientRemoteImportHelper.ShouldStartPaused(state).Should().Be(expected);
    }

    [TestCase(0, 0.5, "pausedDL")]
    [TestCase(0, 1.0, "pausedUP")]
    [TestCase(3, 0.2, "queuedDL")]
    [TestCase(6, 1.0, "uploading")]
    public void MapTransmissionStatusToState_MapsRpcStatus(int status, double progress, string expected)
    {
        DownloadClientRemoteImportHelper.MapTransmissionStatusToState(status, progress).Should().Be(expected);
    }

    [Test]
    public void ResolveImportCategory_PrefersRequestThenRemoteThenClient()
    {
        DownloadClientRemoteImportHelper.ResolveImportCategory("req", "remote", "client").Should().Be("req");
        DownloadClientRemoteImportHelper.ResolveImportCategory(null, "remote", "client").Should().Be("remote");
        DownloadClientRemoteImportHelper.ResolveImportCategory("", "remote", "client").Should().Be("remote");
        DownloadClientRemoteImportHelper.ResolveImportCategory(null, null, "client").Should().Be("client");
    }

    [Test]
    public void ResolveImportSavePath_PrefersRequestThenRemote()
    {
        DownloadClientRemoteImportHelper.ResolveImportSavePath("/req", "/remote").Should().Be("/req");
        DownloadClientRemoteImportHelper.ResolveImportSavePath(null, "/remote").Should().Be("/remote");
        DownloadClientRemoteImportHelper.ResolveImportSavePath("", "/remote").Should().Be("/remote");
        DownloadClientRemoteImportHelper.ResolveImportSavePath(null, null).Should().BeNull();
    }

    [Test]
    public void TryBuildMagnetUri_IncludesDisplayName()
    {
        var hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        DownloadClientRemoteImportHelper.TryBuildMagnetUri(hash, "My Torrent", out var magnet, out var error).Should().BeTrue();
        error.Should().BeNull();
        magnet.Should().Contain("dn=My%20Torrent");
    }
}
