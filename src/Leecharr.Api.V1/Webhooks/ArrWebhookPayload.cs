// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Leecharr.Api.V1.Webhooks;

public class ArrWebhookPayload
{
    [JsonPropertyName("eventType")]
    public string EventType { get; set; }

    [JsonPropertyName("instanceName")]
    public string InstanceName { get; set; }

    [JsonPropertyName("applicationUrl")]
    public string ApplicationUrl { get; set; }

    [JsonPropertyName("downloadClient")]
    public string DownloadClient { get; set; }

    [JsonPropertyName("downloadClientId")]
    public string DownloadClientId { get; set; }

    [JsonPropertyName("downloadId")]
    public string DownloadId { get; set; }

    [JsonPropertyName("sourcePath")]
    public string SourcePath { get; set; }

    [JsonPropertyName("destinationPath")]
    public string DestinationPath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("release")]
    public ArrWebhookRelease Release { get; set; }

    [JsonPropertyName("series")]
    public ArrWebhookSeries Series { get; set; }

    [JsonPropertyName("movie")]
    public ArrWebhookMovie Movie { get; set; }

    [JsonPropertyName("artist")]
    public ArrWebhookArtist Artist { get; set; }

    [JsonPropertyName("album")]
    public ArrWebhookAlbum Album { get; set; }

    [JsonPropertyName("author")]
    public ArrWebhookAuthor Author { get; set; }

    [JsonPropertyName("book")]
    public ArrWebhookBook Book { get; set; }

    [JsonPropertyName("episodeFile")]
    public ArrWebhookEpisodeFile EpisodeFile { get; set; }

    [JsonPropertyName("episodes")]
    public List<ArrWebhookEpisode> Episodes { get; set; } = new();

    [JsonPropertyName("movieFile")]
    public ArrWebhookMovieFile MovieFile { get; set; }

    [JsonPropertyName("trackFiles")]
    public List<ArrWebhookTrackFile> TrackFiles { get; set; } = new();

    [JsonPropertyName("bookFiles")]
    public List<ArrWebhookBookFile> BookFiles { get; set; } = new();

    [JsonPropertyName("renamedFiles")]
    public List<ArrWebhookRenamedFile> RenamedFiles { get; set; } = new();

    [JsonPropertyName("deletedFiles")]
    public List<ArrWebhookDeletedFile> DeletedFiles { get; set; } = new();
}

public class ArrWebhookRelease
{
    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("qualityVersion")]
    public int QualityVersion { get; set; }

    [JsonPropertyName("releaseTitle")]
    public string ReleaseTitle { get; set; }

    [JsonPropertyName("indexer")]
    public string Indexer { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("downloadId")]
    public string DownloadId { get; set; }
}

public class ArrWebhookSeries
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("tvdbId")]
    public int TvdbId { get; set; }

    [JsonPropertyName("tvMazeId")]
    public int TvMazeId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

public class ArrWebhookMovie
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("tmdbId")]
    public int TmdbId { get; set; }

    [JsonPropertyName("imdbId")]
    public string ImdbId { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }
}

public class ArrWebhookArtist
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("mbId")]
    public string MbId { get; set; }
}

public class ArrWebhookAlbum
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }
}

public class ArrWebhookAuthor
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("grid")]
    public string Grid { get; set; }
}

public class ArrWebhookBook
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; }
}

public class ArrWebhookEpisodeFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("qualityVersion")]
    public int QualityVersion { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public string DateAdded { get; set; }
}

public class ArrWebhookEpisode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("episodeNumber")]
    public int EpisodeNumber { get; set; }

    [JsonPropertyName("seasonNumber")]
    public int SeasonNumber { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; }

    [JsonPropertyName("airDate")]
    public string AirDate { get; set; }

    [JsonPropertyName("airDateUtc")]
    public string AirDateUtc { get; set; }
}

public class ArrWebhookMovieFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("qualityVersion")]
    public int QualityVersion { get; set; }

    [JsonPropertyName("releaseGroup")]
    public string ReleaseGroup { get; set; }

    [JsonPropertyName("sceneName")]
    public string SceneName { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("dateAdded")]
    public string DateAdded { get; set; }
}

public class ArrWebhookTrackFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class ArrWebhookBookFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("quality")]
    public string Quality { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class ArrWebhookRenamedFile
{
    [JsonPropertyName("previousPath")]
    public string PreviousPath { get; set; }

    [JsonPropertyName("previousRelativePath")]
    public string PreviousRelativePath { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }
}

public class ArrWebhookDeletedFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; }

    [JsonPropertyName("relativePath")]
    public string RelativePath { get; set; }
}

public class ArrWebhookResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("eventType")]
    public string EventType { get; set; }

    [JsonPropertyName("torrentId")]
    public int? TorrentId { get; set; }

    [JsonPropertyName("infoHash")]
    public string InfoHash { get; set; }

    [JsonPropertyName("updated")]
    public bool Updated { get; set; }
}
