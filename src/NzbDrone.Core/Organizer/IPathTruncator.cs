// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Organizer;

public interface IPathTruncator
{
    string TruncateFileName(string fileName, int maxBytes = PathTruncator.DefaultMaxFileNameBytes);

    string TruncateFolderName(string folderName, int maxBytes = PathTruncator.DefaultMaxFileNameBytes);

    string TruncatePath(string path, int maxPathChars = PathTruncator.DefaultMaxPathChars, int maxComponentBytes = PathTruncator.DefaultMaxFileNameBytes);
}
