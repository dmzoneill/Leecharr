// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Leecharr.Http.REST;

namespace Leecharr.Api.V1.Indexers;

public class IndexerResource : RestResource
{
    public string Name { get; set; }

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

    public string ConfigContract { get; set; }

    public string Settings { get; set; }

    public bool Enable { get; set; } = true;

    public int Priority { get; set; } = 1;

    public string Url { get; set; }

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

    public int MinSeeders { get; set; } = 1;

    public int DownloadClientId { get; set; }

    public List<int> Tags { get; set; } = new();
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

    public int Offset { get; set; } = 0;

    public int Limit { get; set; } = 50;

    public string Type { get; set; }
}
