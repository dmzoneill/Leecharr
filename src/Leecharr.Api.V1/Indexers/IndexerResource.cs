using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Indexers;

public class IndexerResource : RestResource
{
    [Required]
    [StringLength(255, MinimumLength = 1)]
    public string Name { get; set; }

    [StringLength(100)]
    public string Implementation { get; set; } = "Torznab";

    [JsonPropertyName("indexerType")]
    public string IndexerType
    {
        get => this.Implementation;
        set
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                this.Implementation = value;
            }
        }
    }

    [StringLength(100)]
    public string ConfigContract { get; set; }

    public string Settings { get; set; }

    public bool Enable { get; set; } = true;

    [Range(1, int.MaxValue)]
    public int Priority { get; set; } = 1;

    [Required]
    [StringLength(2048)]
    public string Url { get; set; }

    [StringLength(1024)]
    public string ApiKey { get; set; } = string.Empty;

    private List<int> categories = new();

    [JsonConverter(typeof(IntListOrCommaSeparatedConverter))]
    public List<int> Categories
    {
        get => this.categories;
        set => this.categories = value ?? new List<int>();
    }

    public bool EnableRss { get; set; } = true;

    public bool EnableSearch { get; set; } = true;

    public bool FreeleechOnly { get; set; }

    [Range(0, int.MaxValue)]
    public int MinSeeders { get; set; } = 1;

    public int DownloadClientId { get; set; }

    public List<int> Tags { get; set; } = new();

    public int? ProwlarrIndexerId { get; set; }

    public bool IsProwlarrManaged { get; set; }

    public bool SupportsSearch { get; set; } = true;

    public bool SupportsTvSearch { get; set; }

    public bool SupportsMovieSearch { get; set; }

    public bool SupportsMusicSearch { get; set; }

    public bool SupportsBookSearch { get; set; }

    public List<string> SupportedTvParams { get; set; } = new();

    public List<string> SupportedMovieParams { get; set; } = new();

    public List<string> SupportedMusicParams { get; set; } = new();

    public List<string> SupportedBookParams { get; set; } = new();

    public int DefaultPageSize { get; set; } = 50;

    public int MaxPageSize { get; set; } = 100;

    public string Cookie { get; set; }

    public string UserAgent { get; set; }
}

public class IntListOrCommaSeparatedConverter : JsonConverter<List<int>>
{
    public override bool HandleNull => true;

    public override List<int> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return new List<int>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<int>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    break;
                }

                if (reader.TokenType == JsonTokenType.Number)
                {
                    list.Add(reader.GetInt32());
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    var str = reader.GetString();
                    if (int.TryParse(str, out var val))
                    {
                        list.Add(val);
                    }
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return new List<int>();
            }

            var list = new List<int>();
            var parts = str.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (int.TryParse(part, out var val))
                {
                    list.Add(val);
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            return new List<int> { reader.GetInt32() };
        }

        return new List<int>();
    }

    public override void Write(Utf8JsonWriter writer, List<int> value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteNumberValue(item);
        }

        writer.WriteEndArray();
    }
}

public class IndexerTestResult
{
    public bool Success { get; set; }

    public string Message { get; set; }
}

public class DownloadReleaseRequest
{
    public string Title { get; set; }

    public string DownloadUrl { get; set; }

    public string MagnetUrl { get; set; }

    public string InfoHash { get; set; }

    public string Category { get; set; }

    public string SavePath { get; set; }

    public bool StartPaused { get; set; }

    public int? IndexerId { get; set; }

    public string IndexerName { get; set; }

    public string Cookie { get; set; }

    public string UserAgent { get; set; }
}

public class ReleaseInfoResource
{
    public string Title { get; set; }

    public string Guid { get; set; }

    public string Link { get; set; }

    public string Comments { get; set; }

    public DateTime PublishDate { get; set; }

    public string Category { get; set; }

    public long Size { get; set; }

    public string DownloadUrl { get; set; }

    public string MagnetUrl { get; set; }

    public string InfoHash { get; set; }

    public int Seeders { get; set; }

    public int Leechers { get; set; }

    public int IndexerId { get; set; }

    public string IndexerName { get; set; }

    public double DownloadVolumeFactor { get; set; } = 1.0;

    public double UploadVolumeFactor { get; set; } = 1.0;

    public bool IsFreeleech => this.DownloadVolumeFactor <= 0.0;

    public int? ResponseTotal { get; set; }

    public int? ResponseOffset { get; set; }
}

[JsonConverter(typeof(IndexerSearchEnvelopeConverter))]
public class IndexerSearchEnvelope : List<ReleaseInfoResource>
{
    [JsonPropertyName("page")]
    public int Page { get; set; } = 1;

    [JsonPropertyName("limit")]
    public int Limit { get; set; } = 50;

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("results")]
    public List<ReleaseInfoResource> Results => this;

    public IndexerSearchEnvelope()
    {
    }

    public IndexerSearchEnvelope(IEnumerable<ReleaseInfoResource> collection)
        : base(collection)
    {
    }
}

public class IndexerSearchEnvelopeConverter : JsonConverter<IndexerSearchEnvelope>
{
    public override IndexerSearchEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var envelope = new IndexerSearchEnvelope();
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = JsonSerializer.Deserialize<List<ReleaseInfoResource>>(ref reader, options);
            if (list != null)
            {
                envelope.AddRange(list);
                envelope.Total = list.Count;
            }

            return envelope;
        }

        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.TryGetProperty("page", out var pageProp) && pageProp.TryGetInt32(out var page))
        {
            envelope.Page = page;
        }

        if (root.TryGetProperty("limit", out var limitProp) && limitProp.TryGetInt32(out var limit))
        {
            envelope.Limit = limit;
        }

        if (root.TryGetProperty("total", out var totalProp) && totalProp.TryGetInt32(out var total))
        {
            envelope.Total = total;
        }

        if (root.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array)
        {
            var results = JsonSerializer.Deserialize<List<ReleaseInfoResource>>(resultsProp.GetRawText(), options);
            if (results != null)
            {
                envelope.AddRange(results);
            }
        }

        return envelope;
    }

    public override void Write(Utf8JsonWriter writer, IndexerSearchEnvelope value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("page", value.Page);
        writer.WriteNumber("limit", value.Limit);
        writer.WriteNumber("total", value.Total);
        writer.WritePropertyName("results");
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

public class IndexerBatchTestResult
{
    public int Id { get; set; }

    public string Name { get; set; }

    public bool Success { get; set; }

    public string Message { get; set; }

    public long ResponseTimeMs { get; set; }
}

public class IndexerSearchRequest
{
    public string Query { get; set; }

    public string Category { get; set; }

    public int? IndexerId { get; set; }

    public bool FreeleechOnly { get; set; }

    public int? Season { get; set; }

    public int? Ep { get; set; }

    public string ImdbId { get; set; }

    public string TmdbId { get; set; }

    public string TvdbId { get; set; }

    public string Rid { get; set; }

    public int? Year { get; set; }

    public string Artist { get; set; }

    public string Album { get; set; }

    public string Author { get; set; }

    public string Isbn { get; set; }

    public int Offset { get; set; } = 0;

    public int Limit { get; set; } = 50;

    public string Type { get; set; }
}
