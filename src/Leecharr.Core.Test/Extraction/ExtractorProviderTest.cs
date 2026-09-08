// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Extraction;

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class ExtractorProviderTest
{
    #region UnrarExtractorProvider Tests

    [TestCase(@"C:\Downloads\My Torrent\", @"C:\Downloads\My Torrent")]
    [TestCase(@"C:\Downloads\My Torrent\\", @"C:\Downloads\My Torrent")]
    [TestCase(@"C:\Downloads\My Torrent/", @"C:\Downloads\My Torrent")]
    [TestCase(@"C:\Downloads\My Torrent", @"C:\Downloads\My Torrent")]
    [TestCase("/downloads/my torrent/", "/downloads/my torrent")]
    [TestCase("/downloads/my torrent//", "/downloads/my torrent")]
    [TestCase("/downloads/my torrent", "/downloads/my torrent")]
    [TestCase(@"D:\Media\TV Shows\Season 1\", @"D:\Media\TV Shows\Season 1")]
    public void UnrarExtractorProvider_BuildProcessStartInfo_TrimsTrailingSeparatorsFromDestination(string destinationPath, string expectedDestination)
    {
        var startInfo = UnrarExtractorProvider.BuildProcessStartInfo(
            binary: "unrar",
            archivePath: @"C:\Downloads\My Torrent\release.rar",
            destinationPath: destinationPath);

        startInfo.FileName.Should().Be("unrar");
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.CreateNoWindow.Should().BeTrue();
        startInfo.RedirectStandardInput.Should().BeTrue();
        startInfo.RedirectStandardOutput.Should().BeTrue();
        startInfo.RedirectStandardError.Should().BeTrue();

        var args = startInfo.ArgumentList.ToList();
        args.Should().ContainInOrder("x", "-o+", "-y", "-inul", "-p-", @"C:\Downloads\My Torrent\release.rar", expectedDestination);
        args.Last().Should().Be(expectedDestination);
        args.Last().Should().NotEndWith(@"\").And.NotEndWith("/");
    }

    [Test]
    public void UnrarExtractorProvider_BuildProcessStartInfo_WithPassword_SetsPasswordArgument()
    {
        var startInfo = UnrarExtractorProvider.BuildProcessStartInfo(
            binary: "/usr/bin/unrar",
            archivePath: "/downloads/archive.rar",
            destinationPath: "/downloads/extracted/",
            candidatePassword: "SecretPassword123");

        startInfo.RedirectStandardInput.Should().BeTrue();
        var args = startInfo.ArgumentList.ToList();
        args.Should().Contain("-pSecretPassword123");
        args.Should().NotContain("-p-");
        args.Last().Should().Be("/downloads/extracted");
    }

    [TestCase(null)]
    [TestCase("")]
    public void UnrarExtractorProvider_BuildProcessStartInfo_WithoutPassword_SetsNoPasswordSwitch(string candidatePassword)
    {
        var startInfo = UnrarExtractorProvider.BuildProcessStartInfo(
            binary: "/usr/bin/unrar",
            archivePath: "/downloads/archive.rar",
            destinationPath: "/downloads/extracted/",
            candidatePassword: candidatePassword);

        startInfo.RedirectStandardInput.Should().BeTrue();
        var args = startInfo.ArgumentList.ToList();
        args.Should().Contain("-p-");
        args.Should().NotContain(a => a.StartsWith("-p") && a != "-p-");
    }

    #endregion

    #region SevenZipExtractorProvider Tests

    [TestCase(@"C:\Downloads\My Torrent\", @"-oC:\Downloads\My Torrent")]
    [TestCase(@"C:\Downloads\My Torrent\\", @"-oC:\Downloads\My Torrent")]
    [TestCase(@"C:\Downloads\My Torrent/", @"-oC:\Downloads\My Torrent")]
    [TestCase(@"C:\Downloads\My Torrent", @"-oC:\Downloads\My Torrent")]
    [TestCase("/downloads/my torrent/", "-o/downloads/my torrent")]
    [TestCase("/downloads/my torrent//", "-o/downloads/my torrent")]
    [TestCase("/downloads/my torrent", "-o/downloads/my torrent")]
    [TestCase(@"D:\Media\TV Shows\Season 1\", @"-oD:\Media\TV Shows\Season 1")]
    public void SevenZipExtractorProvider_BuildProcessStartInfo_TrimsTrailingSeparatorsFromDestination(string destinationPath, string expectedOutputArg)
    {
        var startInfo = SevenZipExtractorProvider.BuildProcessStartInfo(
            binary: "7z",
            archivePath: @"C:\Downloads\My Torrent\release.7z",
            destinationPath: destinationPath);

        startInfo.FileName.Should().Be("7z");
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo.CreateNoWindow.Should().BeTrue();
        startInfo.RedirectStandardInput.Should().BeTrue();
        startInfo.RedirectStandardOutput.Should().BeTrue();
        startInfo.RedirectStandardError.Should().BeTrue();

        var args = startInfo.ArgumentList.ToList();
        args.Should().ContainInOrder("x", "-y", "-p-", expectedOutputArg, @"C:\Downloads\My Torrent\release.7z");
        args.Should().Contain(expectedOutputArg);
        expectedOutputArg.Should().NotEndWith(@"\").And.NotEndWith("/");
    }

    [Test]
    public void SevenZipExtractorProvider_BuildProcessStartInfo_WithPassword_SetsPasswordArgument()
    {
        var startInfo = SevenZipExtractorProvider.BuildProcessStartInfo(
            binary: "/usr/bin/7z",
            archivePath: "/downloads/archive.7z",
            destinationPath: "/downloads/extracted/",
            candidatePassword: "SecretPassword123");

        startInfo.RedirectStandardInput.Should().BeTrue();
        var args = startInfo.ArgumentList.ToList();
        args.Should().Contain("-pSecretPassword123");
        args.Should().NotContain("-p-");
        args.Should().Contain("-o/downloads/extracted");
    }

    [TestCase(null)]
    [TestCase("")]
    public void SevenZipExtractorProvider_BuildProcessStartInfo_WithoutPassword_SetsNoPasswordSwitch(string candidatePassword)
    {
        var startInfo = SevenZipExtractorProvider.BuildProcessStartInfo(
            binary: "/usr/bin/7z",
            archivePath: "/downloads/archive.7z",
            destinationPath: "/downloads/extracted/",
            candidatePassword: candidatePassword);

        startInfo.RedirectStandardInput.Should().BeTrue();
        var args = startInfo.ArgumentList.ToList();
        args.Should().Contain("-p-");
        args.Should().NotContain(a => a.StartsWith("-p") && a != "-p-");
    }

    #endregion
}
