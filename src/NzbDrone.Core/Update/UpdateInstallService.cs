// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.Update;

public sealed class PendingUpdateManifest
{
    public string Version { get; set; }

    public string PayloadRoot { get; set; }

    public string TargetFolder { get; set; }
}

public class UpdateInstallService : IUpdateInstallService
{
    public const long MaxPackageSizeBytes = 500L * 1024 * 1024;

    private const string PendingManifestFileName = "pending.json";

    private readonly IAppFolderInfo appFolderInfo;
    private readonly IDiskProvider diskProvider;
    private readonly ISafeHttpClientService safeHttpClientService;
    private readonly IArchiveExtractorService archiveExtractorService;
    private readonly Logger logger;

    public UpdateInstallService(
        IAppFolderInfo appFolderInfo,
        IDiskProvider diskProvider = null,
        ISafeHttpClientService safeHttpClientService = null,
        IArchiveExtractorService archiveExtractorService = null)
    {
        this.appFolderInfo = appFolderInfo ?? throw new ArgumentNullException(nameof(appFolderInfo));
        this.diskProvider = diskProvider ?? new DiskProvider();
        this.safeHttpClientService = safeHttpClientService ?? new SafeHttpClientService();
        this.archiveExtractorService = archiveExtractorService ?? new ArchiveExtractorService(this.diskProvider);
        this.logger = LogManager.GetCurrentClassLogger();
    }

    public static string GetUpdateFolder(string appDataFolder)
    {
        return Path.Combine(appDataFolder, "Update");
    }

    public static string GetPendingManifestPath(string appDataFolder)
    {
        return Path.Combine(GetUpdateFolder(appDataFolder), PendingManifestFileName);
    }

    public async Task PrepareUpdateAsync(UpdatePackage package, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (OsInfo.IsContainer)
        {
            throw new InvalidOperationException("In-container installs are not supported. Update the container image instead.");
        }

        if (string.IsNullOrWhiteSpace(package.DownloadUrl))
        {
            throw new InvalidOperationException("Update package does not include a download URL.");
        }

        if (string.IsNullOrWhiteSpace(package.FileName))
        {
            throw new InvalidOperationException("Update package does not include a file name.");
        }

        var updateRoot = GetUpdateFolder(this.appFolderInfo.AppDataFolder);
        var versionFolder = Path.Combine(updateRoot, "staging", package.Version ?? "unknown");
        var cacheFolder = Path.Combine(updateRoot, "cache", package.Version ?? "unknown");
        this.diskProvider.EnsureFolder(cacheFolder);
        this.diskProvider.EnsureFolder(versionFolder);

        if (this.diskProvider.FolderExists(versionFolder) && !this.diskProvider.FolderEmpty(versionFolder))
        {
            this.diskProvider.DeleteFolder(versionFolder, recursive: true);
            this.diskProvider.EnsureFolder(versionFolder);
        }

        var archivePath = Path.Combine(cacheFolder, package.FileName);
        this.logger.Info("Downloading Leecharr update {0} from {1}", package.Version, package.DownloadUrl);

        var payload = await this.safeHttpClientService
            .DownloadBytesAsync(package.DownloadUrl, MaxPackageSizeBytes, cancellationToken)
            .ConfigureAwait(false);

        if (payload == null || payload.Length == 0)
        {
            throw new InvalidOperationException("Update download returned an empty payload.");
        }

        await File.WriteAllBytesAsync(archivePath, payload, cancellationToken).ConfigureAwait(false);

        var extracted = await this.archiveExtractorService
            .ExtractArchiveAsync(archivePath, versionFolder, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!extracted)
        {
            throw new InvalidOperationException("Failed to extract the update package.");
        }

        var payloadRoot = ResolvePayloadRoot(versionFolder, this.diskProvider);
        var manifest = new PendingUpdateManifest
        {
            Version = package.Version,
            PayloadRoot = payloadRoot,
            TargetFolder = this.appFolderInfo.StartUpFolder,
        };

        var manifestPath = GetPendingManifestPath(this.appFolderInfo.AppDataFolder);
        this.diskProvider.EnsureFolder(Path.GetDirectoryName(manifestPath));
        this.diskProvider.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));

        this.logger.Info(
            "Staged Leecharr update {0} at {1}; will apply on restart to {2}",
            package.Version,
            payloadRoot,
            manifest.TargetFolder);
    }

    public bool ApplyPendingUpdate()
    {
        var manifestPath = GetPendingManifestPath(this.appFolderInfo.AppDataFolder);
        if (!this.diskProvider.FileExists(manifestPath))
        {
            return false;
        }

        PendingUpdateManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PendingUpdateManifest>(this.diskProvider.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to read pending update manifest at {0}", manifestPath);
            return false;
        }

        if (manifest == null ||
            string.IsNullOrWhiteSpace(manifest.PayloadRoot) ||
            string.IsNullOrWhiteSpace(manifest.TargetFolder) ||
            !this.diskProvider.FolderExists(manifest.PayloadRoot))
        {
            this.logger.Error("Pending update manifest is invalid or staging folder is missing.");
            return false;
        }

        try
        {
            this.logger.Info(
                "Applying Leecharr update {0} from {1} to {2}",
                manifest.Version,
                manifest.PayloadRoot,
                manifest.TargetFolder);

            CopyDirectory(manifest.PayloadRoot, manifest.TargetFolder, this.diskProvider);
            this.diskProvider.DeleteFile(manifestPath);

            var stagingParent = Path.GetDirectoryName(manifest.PayloadRoot);
            if (!string.IsNullOrWhiteSpace(stagingParent) && this.diskProvider.FolderExists(stagingParent))
            {
                this.diskProvider.DeleteFolder(stagingParent, recursive: true);
            }

            this.logger.Info("Successfully applied Leecharr update {0}", manifest.Version);
            return true;
        }
        catch (Exception ex)
        {
            this.logger.Error(ex, "Failed to apply pending Leecharr update {0}", manifest.Version);
            return false;
        }
    }

    public static string ResolvePayloadRoot(string extractDirectory, IDiskProvider diskProvider)
    {
        if (diskProvider == null || !diskProvider.FolderExists(extractDirectory))
        {
            return extractDirectory;
        }

        var subdirectories = diskProvider.GetDirectories(extractDirectory).ToList();
        if (subdirectories.Count == 1)
        {
            var onlyFilesAtRoot = diskProvider.GetFiles(extractDirectory, recursive: false).Any();
            if (!onlyFilesAtRoot)
            {
                return subdirectories[0];
            }
        }

        return extractDirectory;
    }

    public static void CopyDirectory(string sourceDirectory, string targetDirectory, IDiskProvider diskProvider)
    {
        ArgumentNullException.ThrowIfNull(diskProvider);

        diskProvider.EnsureFolder(targetDirectory);

        foreach (var directory in diskProvider.GetDirectories(sourceDirectory))
        {
            var directoryName = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(targetDirectory, directoryName), diskProvider);
        }

        foreach (var file in diskProvider.GetFiles(sourceDirectory, recursive: false))
        {
            var destination = Path.Combine(targetDirectory, Path.GetFileName(file));
            diskProvider.EnsureFolder(Path.GetDirectoryName(destination));
            diskProvider.CopyFile(file, destination, overwrite: true);
        }
    }
}
