// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
}
