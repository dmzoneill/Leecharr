// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Packages;

public class PackageImportOptions
{
    public string TargetRootDir { get; set; }

    public string DestinationPath { get; set; }

    public string DestinationRoot { get; set; }

    public string SourcePrefix { get; set; }

    public string DestinationPrefix { get; set; }

    public Dictionary<string, string> PathRemappings { get; set; } = new();

    public long? MaxUncompressedBytes { get; set; }

    public double MaxCompressionRatio { get; set; } = 50.0;

    public long MinBytesForRatioCheck { get; set; } = 1024 * 1024; // 1 MiB

    public bool RestoreTorrents { get; set; } = true;

    public bool SkipDuplicates { get; set; } = true;

    public bool VerifyFastResume { get; set; } = true;
}

public class PackageImportResult
{
    public bool Success { get; set; } = true;

    public int ImportedTorrentsCount => this.Torrents.Count(t => !t.IsDuplicate);

    public int SkippedDuplicatesCount => this.SkippedDuplicates.Count;

    public List<PackageImportTorrentSummary> Torrents { get; set; } = new();

    public List<string> SkippedDuplicates { get; set; } = new();

    public List<string> ExtractedFiles { get; set; } = new();

    public long TotalBytesExtracted { get; set; }

    public string Message { get; set; }
}

public class PackageImportTorrentSummary
{
    public int Id { get; set; }

    public string Name { get; set; }

    public string InfoHash { get; set; }

    public string Category { get; set; }

    public List<string> Tags { get; set; } = new();

    public long TotalSize { get; set; }

    public bool IsDuplicate { get; set; }

    public string Status { get; set; }

    public string SavePath { get; set; }
}

public interface IPackageImportService
{
    Task<PackageImportResult> ImportPackageAsync(
        Stream archiveStream,
        PackageImportOptions options = null,
        CancellationToken cancellationToken = default);
}
