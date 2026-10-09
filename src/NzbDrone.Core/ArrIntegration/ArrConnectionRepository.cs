// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ArrIntegration;

public class ArrConnectionRepository : BasicRepository<ArrConnectionDefinition>, IArrConnectionRepository
{
    public ArrConnectionRepository(IDatabase database)
        : base(database)
    {
    }

    public IEnumerable<ArrConnectionDefinition> GetEnabled()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<ArrConnectionDefinition>(
                "SELECT * FROM \"ArrConnectionDefinitions\" WHERE \"Enable\" = @Enable ORDER BY \"Priority\"",
                new { Enable = true }));
    }

    public ArrConnectionDefinition GetByType(string arrType)
    {
        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<ArrConnectionDefinition>(
                "SELECT * FROM \"ArrConnectionDefinitions\" WHERE \"ArrType\" = @ArrType",
                new { ArrType = arrType }));
    }

    public ArrConnectionDefinition GetByAffinity(string arrType, string category = null, string tag = null)
    {
        var connections = this.GetEnabled().ToList();
        if (connections.Count == 0)
        {
            connections = this.All().ToList();
        }

        if (connections.Count == 0)
        {
            return null;
        }

        var candidates = connections;
        if (!string.IsNullOrWhiteSpace(arrType))
        {
            var typeMatches = connections.Where(c => string.Equals(c.ArrType, arrType, StringComparison.OrdinalIgnoreCase)).ToList();
            if (typeMatches.Count > 0)
            {
                candidates = typeMatches;
            }
        }

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        return candidates
            .OrderByDescending(c => CalculateAffinityScore(c, arrType, category, tag))
            .ThenBy(c => c.Priority)
            .ThenBy(c => c.Id)
            .FirstOrDefault();
    }

    private static int CalculateAffinityScore(ArrConnectionDefinition conn, string arrType, string category, string tag)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(arrType) && string.Equals(conn.ArrType, arrType, StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim();
            score += ScoreLabelMatch(conn.Category, conn.Name, cat, categoryExact: 60, nameExact: 50, nameContains: 30);
            score += GetMediaTypeKeywordBonus(conn, cat);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim();
            score += ScoreLabelMatch(label: null, conn.Name, t, categoryExact: 0, nameExact: 40, nameContains: 25);
            score += GetMediaTypeKeywordBonus(conn, t);
        }

        return score;
    }

    private static int ScoreLabelMatch(string label, string name, string value, int categoryExact, int nameExact, int nameContains)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(label) && string.Equals(label.Trim(), value, StringComparison.OrdinalIgnoreCase))
        {
            score += categoryExact;
        }

        if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
        {
            score += nameExact;
        }
        else if (!string.IsNullOrEmpty(name) && name.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            score += nameContains;
        }

        return score;
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(word))
        {
            return false;
        }

        var idx = 0;
        while (idx <= text.Length - word.Length)
        {
            var pos = text.IndexOf(word, idx, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                return false;
            }

            var beforeOk = pos == 0 || !char.IsLetterOrDigit(text[pos - 1]);
            var afterPos = pos + word.Length;
            var afterOk = afterPos >= text.Length || !char.IsLetterOrDigit(text[afterPos]);
            if (beforeOk && afterOk)
            {
                return true;
            }

            idx = pos + 1;
        }

        return false;
    }

    private static int GetMediaTypeKeywordBonus(ArrConnectionDefinition conn, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var s = text.Trim();
        if ((ContainsWholeWord(s, "tv") || ContainsWholeWord(s, "series") || ContainsWholeWord(s, "show") || ContainsWholeWord(s, "sonarr")) &&
            string.Equals(conn.ArrType, "Sonarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if ((ContainsWholeWord(s, "movie") || ContainsWholeWord(s, "film") || ContainsWholeWord(s, "radarr")) &&
            string.Equals(conn.ArrType, "Radarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if ((ContainsWholeWord(s, "music") || ContainsWholeWord(s, "audio") || ContainsWholeWord(s, "flac") || ContainsWholeWord(s, "lidarr")) &&
            string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if ((ContainsWholeWord(s, "book") || ContainsWholeWord(s, "read") || ContainsWholeWord(s, "ebook") || ContainsWholeWord(s, "readarr")) &&
            string.Equals(conn.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        return 0;
    }
}
