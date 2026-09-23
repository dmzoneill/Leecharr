// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Packages;

public interface IPackageExportService
{
    Task ExportPackageAsync(Stream outputStream, IEnumerable<int> torrentIds, bool includePayload, CancellationToken cancellationToken = default);
}
