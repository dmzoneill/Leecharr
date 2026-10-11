// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NzbDrone.Core.MediaEnrichment;

public static class TorrentMediaCastCodec
{
    public static string Serialize(IEnumerable<string> cast)
    {
        if (cast == null)
        {
            return null;
        }

        var entries = cast
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .ToList();

        if (entries.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(entries);
    }

    public static List<string> Parse(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return new List<string>();
        }

        var trimmed = stored.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(trimmed);
                if (parsed != null)
                {
                    return parsed
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c.Trim())
                        .ToList();
                }
            }
            catch (JsonException)
            {
            }
        }

        return trimmed
            .Split(',')
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .ToList();
    }
}
