// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Trackers.Metrics;

public interface ITrackerMetricRepository : IBasicRepository<TrackerMetric>
{
    TrackerMetric FindByUrl(string url);

    List<TrackerMetric> GetAllByUpload();

    List<TrackerMetric> GetByDomain(string domain);

    void ResetStats(int id);
}

public class TrackerMetricRepository : BasicRepository<TrackerMetric>, ITrackerMetricRepository
{
    public TrackerMetricRepository(IDatabase database)
        : base(database)
    {
    }

    public TrackerMetric FindByUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return this.ExecuteWithRetry(connection =>
            connection.QueryFirstOrDefault<TrackerMetric>(
                $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"TrackerUrl\") = LOWER(@Url)",
                new { Url = url.Trim() }));
    }

    public List<TrackerMetric> GetAllByUpload()
    {
        return this.ExecuteWithRetry(connection =>
            connection.Query<TrackerMetric>(
                $"SELECT * FROM \"{this.table}\" ORDER BY \"TotalUploaded\" DESC, \"TotalAnnounces\" DESC")
                .ToList());
    }

    public List<TrackerMetric> GetByDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return new List<TrackerMetric>();
        }

        return this.ExecuteWithRetry(connection =>
            connection.Query<TrackerMetric>(
                $"SELECT * FROM \"{this.table}\" WHERE LOWER(\"Domain\") = LOWER(@Domain) ORDER BY \"TotalUploaded\" DESC",
                new { Domain = domain.Trim() })
                .ToList());
    }

    public void ResetStats(int id)
    {
        this.ExecuteWithRetry(connection =>
            connection.Execute(
                $"UPDATE \"{this.table}\" SET \"TotalAnnounces\" = 0, \"SuccessfulAnnounces\" = 0, \"FailedAnnounces\" = 0, \"TotalScrapes\" = 0, \"SuccessfulScrapes\" = 0, \"FailedScrapes\" = 0, \"TotalUploaded\" = 0, \"TotalDownloaded\" = 0, \"SessionUploaded\" = 0, \"SessionDownloaded\" = 0, \"TotalPeersDiscovered\" = 0 WHERE \"Id\" = @Id",
                new { Id = id }));
    }
}
