// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.DiskSpace;

public class DiskSpaceInfo
{
    public string Path { get; set; }

    public string Label { get; set; }

    public long FreeSpace { get; set; }

    public long TotalSpace { get; set; }

    public string FileSystemType { get; set; } = string.Empty;

    public bool IsReadOnly { get; set; }
}
