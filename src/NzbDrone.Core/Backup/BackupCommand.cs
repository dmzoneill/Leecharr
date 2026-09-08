// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Backup;

public class BackupCommand : Command
{
    public string Type { get; set; } = "Scheduled";
}
