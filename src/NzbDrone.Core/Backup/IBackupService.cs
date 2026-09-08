// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Backup;

public class BackupResult
{
    public string Name { get; set; }

    public string Path { get; set; }

    public string Type { get; set; } = "Manual";

    public long Size { get; set; }

    public DateTime Time { get; set; } = DateTime.UtcNow;

    public string DatabaseType { get; set; } = "SQLite";

    public bool IncludesDatabase { get; set; } = true;
}

public interface IBackupService
{
    BackupResult CreateBackup(string type = "Manual");

    int PruneOldBackups(string backupDirectory = null, int maxBackupsToKeep = 14, int maxAgeDays = 28);
}
