// Copyright (c) FeedItOut. All rights reserved.

using System.Text.Json;

namespace NzbDrone.Core.MediaEnrichment.Providers;

internal static class ServarrRatingsJsonParser
{
    private static readonly string[] NestedSourcePriority =
    {
        "imdb",
        "tmdb",
        "rottenTomatoes",
        "metacritic",
        "trakt",
    };

    public static bool TryGetRating(JsonElement ratings, out double rating)
    {
        rating = 0;
        if (ratings.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (ratings.TryGetProperty("value", out var flatValue) &&
            flatValue.ValueKind == JsonValueKind.Number &&
            flatValue.TryGetDouble(out var flatRating) &&
            flatRating > 0)
        {
            rating = flatRating;
            return true;
        }

        foreach (var sourceName in NestedSourcePriority)
        {
            if (!ratings.TryGetProperty(sourceName, out var source) || source.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (source.TryGetProperty("value", out var nestedValue) &&
                nestedValue.ValueKind == JsonValueKind.Number &&
                nestedValue.TryGetDouble(out var nestedRating) &&
                nestedRating > 0)
            {
                rating = nestedRating;
                return true;
            }
        }

        foreach (var property in ratings.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (property.Value.TryGetProperty("value", out var anyValue) &&
                anyValue.ValueKind == JsonValueKind.Number &&
                anyValue.TryGetDouble(out var anyRating) &&
                anyRating > 0)
            {
                rating = anyRating;
                return true;
            }
        }

        return false;
    }
}
