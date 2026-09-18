// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
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

    #region Secondary Volume and CanExtract Tests

    [TestCase("sample.rar", true)]
    [TestCase("sample.part1.rar", true)]
    [TestCase("sample.part01.rar", true)]
    [TestCase("sample.001", true)]
    [TestCase("sample.r00", false)]
    [TestCase("sample.cbr", true)]
    [TestCase("sample.part2.rar", false)]
    [TestCase("sample.part02.rar", false)]
    [TestCase("sample.r01", false)]
    [TestCase("sample.r02", false)]
    [TestCase("sample.002", false)]
    [TestCase("sample.mkv", false)]
    public void UnrarExtractorProvider_CanExtract_AcceptsPrimaryAndRejectsSecondaryVolumes(string filePath, bool expected)
    {
        var diskProvider = Substitute.For<IDiskProvider>();
        var provider = new UnrarExtractorProvider(diskProvider);
        provider.CanExtract(filePath).Should().Be(expected);
    }

    [TestCase("sample.zip", true)]
    [TestCase("sample.rar", true)]
    [TestCase("sample.7z", true)]
    [TestCase("sample.tar", true)]
    [TestCase("sample.gz", true)]
    [TestCase("sample.part1.rar", true)]
    [TestCase("sample.001", true)]
    [TestCase("sample.part2.rar", false)]
    [TestCase("sample.part02.rar", false)]
    [TestCase("sample.r01", false)]
    [TestCase("sample.part02.7z", false)]
    [TestCase("sample.002", false)]
    [TestCase("sample.mkv", false)]
    public void SharpCompressExtractorProvider_CanExtract_AcceptsPrimaryAndRejectsSecondaryVolumes(string filePath, bool expected)
    {
        var diskProvider = Substitute.For<IDiskProvider>();
        var provider = new SharpCompressExtractorProvider(diskProvider);
        provider.CanExtract(filePath).Should().Be(expected);
    }

    [Test]
    public async Task UnrarExtractorProvider_ExtractAsync_TreatsNonZeroExitCodeAsFailure()
    {
        var prevUnrar = Environment.GetEnvironmentVariable("UNRAR_PATH");
        try
        {
            Environment.SetEnvironmentVariable("UNRAR_PATH", "/usr/bin/false");
            var diskProvider = Substitute.For<IDiskProvider>();
            var provider = new UnrarExtractorProvider(diskProvider);

            provider.IsAvailable.Should().BeTrue();
            var success = await provider.ExtractAsync("/tmp/fake.rar", "/tmp/extracted");
            success.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("UNRAR_PATH", prevUnrar);
        }
    }

    [Test]
    public async Task SharpCompressExtractorProvider_RollsBackCreatedFiles_WhenExtractionFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sharpcompress_test_" + Guid.NewGuid().ToString("N"));
        var outputDir = Path.Combine(tempDir, "output");
        Directory.CreateDirectory(outputDir);
        var zipPath = Path.Combine(tempDir, "corrupted.zip");

        try
        {
            using (var zipStream = new FileStream(zipPath, FileMode.Create))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("payload.txt");
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                await writer.WriteAsync(new string('a', 50000));
            }

            // Read the zip file and truncate it partially to cause an extraction error mid-stream
            var bytes = await File.ReadAllBytesAsync(zipPath);
            // Truncate some bytes from the end so uncompressing the entry stream fails
            var truncatedBytes = bytes.Take(bytes.Length - 50).ToArray();
            await File.WriteAllBytesAsync(zipPath, truncatedBytes);

            var diskProvider = new DiskProvider();
            var provider = new SharpCompressExtractorProvider(diskProvider);

            var success = await provider.ExtractAsync(zipPath, outputDir);
            success.Should().BeFalse();

            var expectedFile = Path.Combine(outputDir, "payload.txt");
            File.Exists(expectedFile).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

}
