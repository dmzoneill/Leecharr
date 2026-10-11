// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Ai;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace Leecharr.Core.Test.Ai;

[TestFixture]
public class AiControllerTest
{
    private IAiService aiService = null!;
    private IAiManager aiManager = null!;
    private ITorrentService torrentService = null!;
    private ITorrentFileService torrentFileService = null!;
    private IDownloadEngine downloadEngine = null!;
    private ITrackerEntryRepository trackerRepo = null!;
    private AiController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.aiService = Substitute.For<IAiService, IFallbackAwareAiProvider>();
        this.aiManager = Substitute.For<IAiManager, IFallbackAwareAiProvider>();
        this.torrentService = Substitute.For<ITorrentService>();
        this.torrentFileService = Substitute.For<ITorrentFileService>();
        this.downloadEngine = Substitute.For<IDownloadEngine>();
        this.trackerRepo = Substitute.For<ITrackerEntryRepository>();

        this.controller = new AiController(
            this.aiService,
            this.aiManager,
            this.torrentService,
            this.torrentFileService,
            this.downloadEngine,
            this.trackerRepo);
    }

    [Test]
    public async Task CheckMalwareRisk_WhenTorrentIdNotFound_ReturnsNotFound()
    {
        const int missingTorrentId = 999999;
        this.torrentService.Get(missingTorrentId).Returns((Torrent)null!);

        var result = await this.controller.CheckMalwareRisk(new AiMalwareCheckRequest
        {
            TorrentId = missingTorrentId,
        });

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        await this.aiService.DidNotReceive().AnalyzeMalwareRiskAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<TorrentFile>>());
    }

    [Test]
    public async Task Chat_WhenRequestMessageEmpty_ReturnsBadRequest()
    {
        var result = await this.controller.Chat(new AiChatRequest { Message = string.Empty });
        var badRequest = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequest.Value.Should().BeOfType<AiChatResponse>().Subject;
        response.Success.Should().BeFalse();
        response.Error.Should().Be("Message is required.");
    }

    [Test]
    public async Task Chat_WhenLlmSucceeds_ReturnsProviderDisplayName()
    {
        var provider = Substitute.For<IAiEngineProvider>();
        provider.ProviderId.Returns("Ollama");
        provider.DisplayName.Returns("Ollama Local LLM Sidecar");

        this.aiManager.ActiveProvider.Returns(provider);
        this.aiManager.ActiveProviderId.Returns("Ollama");
        ((IFallbackAwareAiProvider)this.aiService).LastChatUsedFallback.Returns(false);
        this.aiService.GenerateChatResponseAsync("Hello").Returns(Task.FromResult("Ollama response"));

        var result = await this.controller.Chat(new AiChatRequest { Message = "Hello" });
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AiChatResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Reply.Should().Be("Ollama response");
        response.Provider.Should().Be("Ollama Local LLM Sidecar");
    }

    [Test]
    public async Task Chat_WhenLlmFailsAndFallsBackToHeuristics_AppendsFallbackHeuristicsToProvider()
    {
        var provider = Substitute.For<IAiEngineProvider>();
        provider.ProviderId.Returns("Ollama");
        provider.DisplayName.Returns("Ollama Local LLM Sidecar");

        this.aiManager.ActiveProvider.Returns(provider);
        this.aiManager.ActiveProviderId.Returns("Ollama");
        ((IFallbackAwareAiProvider)this.aiService).LastChatUsedFallback.Returns(true);
        this.aiService.GenerateChatResponseAsync("Hello").Returns(Task.FromResult("Heuristic fallback response"));

        var result = await this.controller.Chat(new AiChatRequest { Message = "Hello" });
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AiChatResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Reply.Should().Be("Heuristic fallback response");
        response.Provider.Should().Be("Ollama Local LLM Sidecar (Fallback Heuristics)");
    }

    [Test]
    public async Task Chat_WhenActiveProviderIsRuleHeuristic_DoesNotAppendDuplicateFallbackHeuristics()
    {
        var provider = Substitute.For<IAiEngineProvider>();
        provider.ProviderId.Returns("RuleHeuristic");
        provider.DisplayName.Returns("Rule-Based Heuristic AI");

        this.aiManager.ActiveProvider.Returns(provider);
        this.aiManager.ActiveProviderId.Returns("RuleHeuristic");
        ((IFallbackAwareAiProvider)this.aiService).LastChatUsedFallback.Returns(true);
        this.aiService.GenerateChatResponseAsync("Hello").Returns(Task.FromResult("Rule response"));

        var result = await this.controller.Chat(new AiChatRequest { Message = "Hello" });
        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<AiChatResponse>().Subject;
        response.Success.Should().BeTrue();
        response.Reply.Should().Be("Rule response");
        response.Provider.Should().Be("Rule-Based Heuristic AI");
    }

    [Test]
    public async Task CheckMalwareRisk_WhenTorrentHasDbFiles_IncludesRequestFileNames()
    {
        const int torrentId = 5;
        this.torrentService.Get(torrentId).Returns(new Torrent { Id = torrentId, Name = "Movie.1080p.mkv" });
        this.torrentFileService.GetFiles(torrentId).Returns(new List<TorrentFile>
        {
            new TorrentFile { TorrentId = torrentId, Path = "Movie.1080p.mkv", Size = 4_000_000_000 },
        });
        this.aiService.AnalyzeMalwareRiskAsync(Arg.Any<string>(), Arg.Any<List<TorrentFile>>())
            .Returns(Task.FromResult(new AiMalwareRiskAssessment { RiskLevel = "Safe" }));

        var result = await this.controller.CheckMalwareRisk(new AiMalwareCheckRequest
        {
            TorrentId = torrentId,
            FileNames = new List<string> { "codec_setup.exe" },
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        await this.aiService.Received().AnalyzeMalwareRiskAsync(
            "Movie.1080p.mkv",
            Arg.Is<List<TorrentFile>>(files =>
                files.Count == 2
                && files.Any(f => f.Path == "Movie.1080p.mkv")
                && files.Any(f => f.Path == "codec_setup.exe" && f.Size == 1024 * 1024)));
    }
}
