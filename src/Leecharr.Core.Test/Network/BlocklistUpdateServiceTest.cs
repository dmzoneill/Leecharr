// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Http;
using NzbDrone.Core.Network.Blocklist;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class BlocklistUpdateServiceTest
{
    private IBlocklistService blocklistService = null!;
    private IConfigService configService = null!;
    private ISafeHttpClientService safeHttpClientService = null!;
    private BlocklistUpdateService updateService = null!;

    [SetUp]
    public void SetUp()
    {
        this.blocklistService = Substitute.For<IBlocklistService>();
        this.configService = Substitute.For<IConfigService>();
        this.safeHttpClientService = Substitute.For<ISafeHttpClientService>();

        this.updateService = new BlocklistUpdateService(
            this.blocklistService,
            this.configService,
            this.safeHttpClientService);
    }

    [Test]
    public async Task UpdateRulesAsync_WhenDisabled_ReturnsZeroWithoutCallingProviders()
    {
        this.configService.BlocklistEnabled.Returns(false);

        var result = await this.updateService.UpdateRulesAsync();

        result.Should().Be(0);
        await this.blocklistService.DidNotReceive().LoadRulesAsync(Arg.Any<IEnumerable<string>>());
    }

    [Test]
    public async Task UpdateRulesAsync_WithValidUrl_DownloadsAndLoadsRules()
    {
        this.configService.BlocklistEnabled.Returns(true);
        this.configService.BlocklistUrl.Returns("https://example.com/blocklist.txt");
        this.configService.BlocklistPath.Returns(string.Empty);

        var rawContent = "1.2.3.4/32\n5.6.7.8/24\n# Comment line\n";
        var bytes = Encoding.UTF8.GetBytes(rawContent);

        this.safeHttpClientService.DownloadBytesAsync("https://example.com/blocklist.txt", Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(bytes));

        this.blocklistService.LoadRulesAsync(Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult(2));

        var result = await this.updateService.UpdateRulesAsync();

        result.Should().Be(2);
        await this.blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r != null));
    }

    [Test]
    public async Task UpdateRulesAsync_WithGzippedFeed_DecompressesAndLoadsRules()
    {
        this.configService.BlocklistEnabled.Returns(true);
        this.configService.BlocklistUrl.Returns("https://example.com/blocklist.gz");

        var rawText = "10.0.0.0/8\n192.168.0.0/16\n";
        byte[] gzipped;
        using (var ms = new MemoryStream())
        {
            using (var gz = new GZipStream(ms, CompressionMode.Compress))
            {
                var textBytes = Encoding.UTF8.GetBytes(rawText);
                gz.Write(textBytes, 0, textBytes.Length);
            }

            gzipped = ms.ToArray();
        }

        this.safeHttpClientService.DownloadBytesAsync("https://example.com/blocklist.gz", Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(gzipped));

        this.blocklistService.LoadRulesAsync(Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult(2));

        var result = await this.updateService.UpdateRulesAsync();

        result.Should().Be(2);
    }

    [Test]
    public async Task UpdateRulesAsync_WithZipFeed_DecompressesAndLoadsRules()
    {
        this.configService.BlocklistEnabled.Returns(true);
        this.configService.BlocklistUrl.Returns("https://example.com/blocklist.zip");

        var rawText = "172.16.0.0/12\n10.10.0.0/16\n";
        byte[] zipped;
        using (var ms = new MemoryStream())
        {
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry("level1.txt");
                using var entryStream = entry.Open();
                var textBytes = Encoding.UTF8.GetBytes(rawText);
                entryStream.Write(textBytes, 0, textBytes.Length);
            }

            zipped = ms.ToArray();
        }

        this.safeHttpClientService.DownloadBytesAsync("https://example.com/blocklist.zip", Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(zipped));

        this.blocklistService.LoadRulesAsync(Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult(2));

        var result = await this.updateService.UpdateRulesAsync();

        result.Should().Be(2);
        await this.blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r != null));
    }

    [Test]
    public async Task UpdateRulesAsync_WithLocalFilePath_StreamsFileAndLoadsRules()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "1.1.1.1\n8.8.8.8\n\n# comment\n");

            this.configService.BlocklistEnabled.Returns(true);
            this.configService.BlocklistPath.Returns(tempFile);
            this.configService.BlocklistUrl.Returns(string.Empty);

            this.blocklistService.LoadRulesAsync(Arg.Any<IEnumerable<string>>())
                .Returns(callInfo =>
                {
                    var rules = callInfo.Arg<IEnumerable<string>>().ToList();
                    return Task.FromResult(rules.Count);
                });

            var result = await this.updateService.UpdateRulesAsync();

            result.Should().Be(2);
            await this.blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r != null));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task UpdateRulesAsync_WithLocalGzipFile_DecompressesAndLoadsRules()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"{System.Guid.NewGuid():N}.gz");
        try
        {
            using (var fs = File.Create(tempFile))
            using (var gz = new GZipStream(fs, CompressionMode.Compress))
            {
                var bytes = Encoding.UTF8.GetBytes("10.0.0.1/32\n10.0.0.2/32\n10.0.0.3/32\n");
                gz.Write(bytes, 0, bytes.Length);
            }

            this.configService.BlocklistEnabled.Returns(true);
            this.configService.BlocklistPath.Returns(tempFile);
            this.configService.BlocklistUrl.Returns(string.Empty);

            this.blocklistService.LoadRulesAsync(Arg.Any<IEnumerable<string>>())
                .Returns(callInfo =>
                {
                    var rules = callInfo.Arg<IEnumerable<string>>().ToList();
                    return Task.FromResult(rules.Count);
                });

            var result = await this.updateService.UpdateRulesAsync();

            result.Should().Be(3);
            await this.blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r != null));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public async Task UpdateRulesAsync_WithBothLocalFileAndUrl_StreamsCombinedRules()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "1.1.1.1\n");

            this.configService.BlocklistEnabled.Returns(true);
            this.configService.BlocklistPath.Returns(tempFile);
            this.configService.BlocklistUrl.Returns("https://example.com/blocklist.txt");

            var urlBytes = Encoding.UTF8.GetBytes("2.2.2.2\n3.3.3.3\n");
            this.safeHttpClientService.DownloadBytesAsync("https://example.com/blocklist.txt", Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(urlBytes));

            this.blocklistService.LoadRulesAsync(Arg.Any<IEnumerable<string>>())
                .Returns(callInfo =>
                {
                    var rules = callInfo.Arg<IEnumerable<string>>().ToList();
                    return Task.FromResult(rules.Count);
                });

            var result = await this.updateService.UpdateRulesAsync();

            result.Should().Be(3);
            await this.blocklistService.Received(1).LoadRulesAsync(Arg.Is<IEnumerable<string>>(r => r != null));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void ParseLines_LargeStream_StreamsLazilyWithoutMaterializingList()
    {
        var lineCount = 100_000;
        var sb = new StringBuilder();
        for (var i = 0; i < lineCount; i++)
        {
            sb.AppendLine($"10.0.{i / 256}.{i % 256}/32");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        using var stream = new MemoryStream(bytes);

        var count = 0;
        foreach (var line in BlocklistUpdateService.ParseLines(stream))
        {
            count++;
            line.Should().StartWith("10.0.");
        }

        count.Should().Be(lineCount);
    }

    [Test]
    public void ParseLines_WithNullOrEmpty_YieldsBreak()
    {
        BlocklistUpdateService.ParseLines((byte[])null!).Should().BeEmpty();
        BlocklistUpdateService.ParseLines(System.Array.Empty<byte>()).Should().BeEmpty();
        BlocklistUpdateService.ParseLines((Stream)null!).Should().BeEmpty();
    }

    [Test]
    public void ParseLines_WithWhitespaceAndEmptyLines_FiltersOutWhitespace()
    {
        var content = "  \n  1.2.3.4  \n\n\r\n   5.6.7.8   \n  \t  \n";
        var bytes = Encoding.UTF8.GetBytes(content);

        var lines = BlocklistUpdateService.ParseLines(bytes).ToList();

        lines.Should().Equal("1.2.3.4", "5.6.7.8");
    }
}
