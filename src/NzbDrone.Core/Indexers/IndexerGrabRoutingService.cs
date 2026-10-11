// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Http;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Indexers;

public class IndexerGrabRequest
{
    public string Title { get; set; }

    public string MagnetUrl { get; set; }

    public string DownloadUrl { get; set; }

    public byte[] TorrentBytes { get; set; }

    public string Category { get; set; }

    public string SavePath { get; set; }

    public bool StartPaused { get; set; }
}

public class IndexerGrabResult
{
    public bool Success { get; set; }

    public bool UsedExternalClient { get; set; }

    public Torrent Torrent { get; set; }

    public string InfoHash { get; set; }

    public string ErrorMessage { get; set; }
}

public interface IIndexerGrabRoutingService
{
    Task<IndexerGrabResult> GrabAsync(int downloadClientId, IndexerGrabRequest request, CancellationToken cancellationToken = default);
}

public class IndexerGrabRoutingService : IIndexerGrabRoutingService
{
    private readonly IDownloadClientRepository downloadClientRepository;
    private readonly ITorrentService torrentService;
    private readonly ITorrentFileParser torrentFileParser;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public IndexerGrabRoutingService(
        IDownloadClientRepository downloadClientRepository,
        ITorrentService torrentService,
        ITorrentFileParser torrentFileParser = null,
        ISafeHttpClientService safeHttpClientService = null,
        HttpClient httpClient = null)
    {
        this.downloadClientRepository = downloadClientRepository;
        this.torrentService = torrentService;
        this.torrentFileParser = torrentFileParser ?? new TorrentFileParser();
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.httpClient = httpClient;
    }

    public async Task<IndexerGrabResult> GrabAsync(int downloadClientId, IndexerGrabRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (downloadClientId > 0)
        {
            var client = this.downloadClientRepository.Get(downloadClientId);
            if (client == null)
            {
                this.logger.Warn("Indexer grab referenced download client {0}, but it was not found", downloadClientId);
                return new IndexerGrabResult
                {
                    Success = false,
                    ErrorMessage = $"Download client {downloadClientId} was not found.",
                };
            }

            if (!client.Enable)
            {
                this.logger.Warn("Indexer grab referenced download client {0}, but it is disabled", downloadClientId);
                return new IndexerGrabResult
                {
                    Success = false,
                    ErrorMessage = $"Download client {downloadClientId} is disabled.",
                };
            }

            var addRequest = new DownloadClientReleaseAddRequest
            {
                MagnetUrl = request.MagnetUrl,
                TorrentUrl = request.DownloadUrl,
                TorrentBytes = request.TorrentBytes,
                Category = request.Category,
                SavePath = request.SavePath,
                StartPaused = request.StartPaused,
            };

            var addResult = await DownloadClientRemoteReleaseSender.AddReleaseAsync(
                client,
                addRequest,
                this.httpClient,
                this.safeHttpClientService,
                cancellationToken);

            if (!addResult.Success)
            {
                return new IndexerGrabResult
                {
                    Success = false,
                    UsedExternalClient = true,
                    ErrorMessage = addResult.ErrorMessage,
                };
            }

            return new IndexerGrabResult
            {
                Success = true,
                UsedExternalClient = true,
                InfoHash = addResult.InfoHash,
            };
        }

        var torrent = await this.AddToEmbeddedEngineAsync(request);
        if (torrent == null)
        {
            return new IndexerGrabResult
            {
                Success = false,
                ErrorMessage = "Failed to add release to the embedded download engine.",
            };
        }

        return new IndexerGrabResult
        {
            Success = true,
            UsedExternalClient = false,
            Torrent = torrent,
            InfoHash = torrent.InfoHash,
        };
    }

    private async Task<Torrent> AddToEmbeddedEngineAsync(IndexerGrabRequest request)
    {
        if (request.TorrentBytes != null && request.TorrentBytes.Length > 0)
        {
            var parsed = this.torrentFileParser.Parse(request.TorrentBytes);
            return await this.torrentService.AddFromParsedTorrentAsync(
                parsed,
                request.Category,
                request.SavePath,
                request.StartPaused,
                request.TorrentBytes);
        }

        if (!string.IsNullOrWhiteSpace(request.MagnetUrl))
        {
            return await this.torrentService.AddFromMagnetAsync(
                request.MagnetUrl,
                request.Category,
                request.SavePath,
                request.StartPaused);
        }

        if (!string.IsNullOrWhiteSpace(request.DownloadUrl) &&
            request.DownloadUrl.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
        {
            return await this.torrentService.AddFromMagnetAsync(
                request.DownloadUrl,
                request.Category,
                request.SavePath,
                request.StartPaused);
        }

        return null;
    }
}
