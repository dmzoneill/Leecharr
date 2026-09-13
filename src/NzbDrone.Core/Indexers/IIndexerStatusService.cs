// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Indexers;

public class IndexerStatus
{
    public int IndexerId { get; set; }

    public DateTime? InitialFailure { get; set; }

    public DateTime? MostRecentFailure { get; set; }

    public DateTime? DisabledTill { get; set; }

    public int ConsecutiveFailures { get; set; }

    public string LastFailureMessage { get; set; }

    public int? LastStatusCode { get; set; }

    public bool IsDisabled => this.DisabledTill.HasValue && this.DisabledTill.Value > DateTime.UtcNow;
}

public interface IIndexerStatusService
{
    void RecordSuccess(int indexerId);

    void RecordFailure(int indexerId, int? statusCode = null, string errorMessage = null, Exception ex = null);

    bool IsDisabled(int indexerId);

    IndexerStatus GetStatus(int indexerId);

    IReadOnlyDictionary<int, IndexerStatus> GetAllStatuses();

    void Reset(int indexerId);

    void ResetAll();

    TimeSpan CalculateBackoff(int consecutiveFailures, int? statusCode = null);
}
