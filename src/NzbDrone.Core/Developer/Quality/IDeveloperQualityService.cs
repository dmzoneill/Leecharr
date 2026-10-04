// Copyright (c) FeedItOut. All rights reserved.

using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Developer.Quality;

public interface IDeveloperQualityService
{
    Task<DeveloperQualityReport> GetQualityReportAsync(CancellationToken cancellationToken = default);
}
