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
            if (string.Equals(conn.Name, cat, StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }
            else if (conn.Name != null && (conn.Name.Contains(cat, StringComparison.OrdinalIgnoreCase) || cat.Contains(conn.Name, StringComparison.OrdinalIgnoreCase)))
            {
                score += 30;
            }

            if (conn.Settings != null && conn.Settings.Contains(cat, StringComparison.OrdinalIgnoreCase))
            {
                score += 25;
            }

            score += GetMediaTypeKeywordBonus(conn, cat);
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var t = tag.Trim();
            if (string.Equals(conn.Name, t, StringComparison.OrdinalIgnoreCase))
            {
                score += 40;
            }
            else if (conn.Name != null && (conn.Name.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(conn.Name, StringComparison.OrdinalIgnoreCase)))
            {
                score += 25;
            }

            if (conn.Settings != null && conn.Settings.Contains(t, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            score += GetMediaTypeKeywordBonus(conn, t);
        }

        return score;
    }

    private static int GetMediaTypeKeywordBonus(ArrConnectionDefinition conn, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var s = text.Trim();
        if ((s.Contains("tv", StringComparison.OrdinalIgnoreCase) || s.Contains("series", StringComparison.OrdinalIgnoreCase) || s.Contains("show", StringComparison.OrdinalIgnoreCase) || s.Contains("sonarr", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(conn.ArrType, "Sonarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if ((s.Contains("movie", StringComparison.OrdinalIgnoreCase) || s.Contains("film", StringComparison.OrdinalIgnoreCase) || s.Contains("radarr", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(conn.ArrType, "Radarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if ((s.Contains("music", StringComparison.OrdinalIgnoreCase) || s.Contains("audio", StringComparison.OrdinalIgnoreCase) || s.Contains("flac", StringComparison.OrdinalIgnoreCase) || s.Contains("lidarr", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(conn.ArrType, "Lidarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        if ((s.Contains("book", StringComparison.OrdinalIgnoreCase) || s.Contains("read", StringComparison.OrdinalIgnoreCase) || s.Contains("ebook", StringComparison.OrdinalIgnoreCase) || s.Contains("readarr", StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(conn.ArrType, "Readarr", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }

        return 0;
    }
}
