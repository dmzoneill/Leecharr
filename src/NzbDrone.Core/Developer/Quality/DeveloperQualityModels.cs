// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Developer.Quality;

public class QualityGateMetric
{
    public string MetricKey { get; set; }

    public string Status { get; set; }

    public string ActualValue { get; set; }

    public string ErrorThreshold { get; set; }

    public string Comparator { get; set; }
}

public class QualityGateStatus
{
    public string Status { get; set; } = "OK";

    public List<QualityGateMetric> Conditions { get; set; } = new();
}

public class QualityOverviewMetrics
{
    public double Coverage { get; set; }

    public double NewCodeCoverage { get; set; }

    public double DuplicationDensity { get; set; }

    public int Bugs { get; set; }

    public int Vulnerabilities { get; set; }

    public int CodeSmells { get; set; }

    public double SecurityHotspotsReviewed { get; set; }

    public string ReliabilityRating { get; set; } = "A";

    public string SecurityRating { get; set; } = "A";

    public string MaintainabilityRating { get; set; } = "A";

    public string TechnicalDebtFormatted { get; set; } = "0 min";
}

public class QualityPipelineRun
{
    public string Name { get; set; }

    public string Status { get; set; }

    public string Conclusion { get; set; }

    public int RunNumber { get; set; }

    public string HtmlUrl { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class DeveloperQualityReport
{
    public string ProjectName { get; set; }

    public string Repository { get; set; }

    public QualityGateStatus QualityGate { get; set; } = new();

    public QualityOverviewMetrics Metrics { get; set; } = new();

    public List<QualityPipelineRun> RecentPipelines { get; set; } = new();

    public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;

    public string SonarCloudUrl { get; set; }

    public string CodacyUrl { get; set; }

    public string GitHubRepoUrl { get; set; }

    public string Message { get; set; }
}
