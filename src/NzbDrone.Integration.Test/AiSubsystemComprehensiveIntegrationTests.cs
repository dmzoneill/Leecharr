// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class AiSubsystemComprehensiveIntegrationTests : IntegrationTestBase
{
    private const string TestHash = "a111111111111111111111111111111111111111";

    [Test]
    public async Task AiStatus_ReturnsActiveProviderAndCapabilities()
    {
        var response = await this.Client.GetAsync("/api/v1/ai/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        root.GetProperty("activeProviderId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("displayName").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("capabilities").GetProperty("supportsReleaseNameParsing").GetBoolean().Should().BeTrue();
        root.GetProperty("capabilities").GetProperty("supportsDiagnosticCopilot").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task AiParseRelease_ComprehensiveReleaseTypes_ParsedCorrectly()
    {
        // 1. 4K UHD Remux with Dolby Vision and Atmos
        var req1 = new
        {
            releaseName = "Dune.Part.Two.2024.2160p.UHD.Remux.HEVC.DV.TrueHD.Atmos.7.1-FraMeSToR.mkv",
        };
        var resp1 = await this.PostJsonAsync("/api/v1/ai/parse-release", req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        var root1 = doc1.RootElement;

        root1.GetProperty("cleanTitle").GetString().Should().Contain("Dune");
        root1.GetProperty("year").GetInt32().Should().Be(2024);
        root1.GetProperty("resolution").GetString().Should().Be("2160p");
        root1.GetProperty("videoCodec").GetString().Should().Be("x265");
        root1.GetProperty("isRemux").GetBoolean().Should().BeTrue();

        // 2. Multi-episode TV series repack
        var req2 = new
        {
            releaseName = "Shogun.2024.S01E01-E03.REPACK.1080p.WEB-DL.DDP5.1.Atmos.H.264-FLUX",
        };
        var resp2 = await this.PostJsonAsync("/api/v1/ai/parse-release", req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
        var root2 = doc2.RootElement;

        root2.GetProperty("season").GetInt32().Should().Be(1);
        root2.GetProperty("episode").GetInt32().Should().Be(1);
        root2.GetProperty("isRepack").GetBoolean().Should().BeTrue();
        root2.GetProperty("episodes").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);

        // 3. Alternative format 1x05 PROPER
        var req3 = new
        {
            releaseName = "Breaking.Bad.1x05.PROPER.720p.HDTV.x264-CTU",
        };
        var resp3 = await this.PostJsonAsync("/api/v1/ai/parse-release", req3);
        resp3.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc3 = JsonDocument.Parse(await resp3.Content.ReadAsStringAsync());
        doc3.RootElement.GetProperty("isProper").GetBoolean().Should().BeTrue();

        // 4. Empty release name validation
        var emptyResp = await this.PostJsonAsync("/api/v1/ai/parse-release", new { releaseName = "   " });
        emptyResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AiNaturalSearch_TranslatesQueriesToSearchParameters()
    {
        // 1. Search for 4K movies with seeders
        var req1 = new
        {
            query = "find 4k action movies from 2023 with minimum 10 seeders",
        };
        var resp1 = await this.PostJsonAsync("/api/v1/ai/natural-search", req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        var root1 = doc1.RootElement;

        root1.GetProperty("resolution").GetString().Should().Be("2160p");
        root1.GetProperty("year").GetInt32().Should().Be(2023);

        // 2. Search for TV season 2 1080p
        var req2 = new
        {
            query = "download season 2 of Severance in 1080p web-dl",
        };
        var resp2 = await this.PostJsonAsync("/api/v1/ai/natural-search", req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
        var root2 = doc2.RootElement;

        root2.GetProperty("season").GetInt32().Should().Be(2);
        root2.GetProperty("resolution").GetString().Should().Be("1080p");

        // 3. Empty query validation
        var emptyResp = await this.PostJsonAsync("/api/v1/ai/natural-search", new { query = "" });
        emptyResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AiMalwareCheck_IdentifiesSafeAndDangerousFiles()
    {
        // 1. Safe media files
        var safeReq = new
        {
            torrentName = "Big.Buck.Bunny.1080p.mkv",
            fileNames = new List<string>
            {
                "Big.Buck.Bunny.1080p.mkv",
                "Subtitles/English.srt",
                "cover.jpg",
            },
        };
        var safeResp = await this.PostJsonAsync("/api/v1/ai/malware-check", safeReq);
        safeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var safeDoc = JsonDocument.Parse(await safeResp.Content.ReadAsStringAsync());
        safeDoc.RootElement.GetProperty("riskLevel").GetString().Should().Be("Safe");

        // 2. Suspicious files (.exe masquerading as video, .scr, password files)
        var dangerousReq = new
        {
            torrentName = "Latest.Movie.2024.CAM.x264",
            fileNames = new List<string>
            {
                "Latest.Movie.2024.CAM.mkv.exe",
                "codec_setup.bat",
                "password_to_extract.txt",
                "screensaver.scr",
            },
        };
        var dangerousResp = await this.PostJsonAsync("/api/v1/ai/malware-check", dangerousReq);
        dangerousResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dangerDoc = JsonDocument.Parse(await dangerousResp.Content.ReadAsStringAsync());
        var dangerLevel = dangerDoc.RootElement.GetProperty("riskLevel").GetString();
        dangerLevel.Should().NotBe("Clean");
        dangerDoc.RootElement.GetProperty("riskScore").GetDouble().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task AiChat_RespondsToTorrentAndNetworkingQuestions()
    {
        // 1. Ratio advice
        var req1 = new
        {
            message = "How can I improve my seeding ratio on private trackers?",
        };
        var resp1 = await this.PostJsonAsync("/api/v1/ai/chat", req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
        doc1.RootElement.GetProperty("reply").GetString().Should().NotBeNullOrWhiteSpace();

        // 2. Port forwarding advice
        var req2 = new
        {
            message = "Why is my incoming port closed and how do I configure port forwarding?",
        };
        var resp2 = await this.PostJsonAsync("/api/v1/ai/chat", req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
        doc2.RootElement.GetProperty("reply").GetString().Should().NotBeNullOrWhiteSpace();

        // 3. Validation for empty message
        var emptyResp = await this.PostJsonAsync("/api/v1/ai/chat", new { message = "" });
        emptyResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AiDiagnose_ExistingTorrent_GeneratesDiagnosticReport()
    {
        // Add a temporary torrent
        var addResp = await this.PostJsonAsync("/api/v1/torrents", new
        {
            magnetLink = $"magnet:?xt=urn:btih:{TestHash}&dn=AiDiagnoseMovie",
            category = "movies",
            paused = true,
        });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var addDoc = JsonDocument.Parse(await addResp.Content.ReadAsStringAsync());
        var torrentId = addDoc.RootElement.GetProperty("id").GetInt32();

        try
        {
            // Diagnose torrent
            var diagResp = await this.PostJsonAsync($"/api/v1/ai/diagnose/{torrentId}", new { });
            diagResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var diagDoc = JsonDocument.Parse(await diagResp.Content.ReadAsStringAsync());
            diagDoc.RootElement.GetProperty("healthScore").GetDouble().Should().BeGreaterThanOrEqualTo(0);

            // Non-existent torrent ID returns 404
            var notFoundResp = await this.PostJsonAsync("/api/v1/ai/diagnose/999999", new { });
            notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await this.DeleteAsync($"/api/v1/torrents/{torrentId}?deleteFiles=false");
        }
    }
}
