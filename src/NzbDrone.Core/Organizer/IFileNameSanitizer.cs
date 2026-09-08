// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Organizer;

public interface IFileNameSanitizer
{
    string SanitizeFileName(string fileName, ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace, string customColon = null);

    string SanitizeFolderName(string folderName, ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace, string customColon = null);

    string SanitizePath(string path, ColonReplacementFormat colonFormat = ColonReplacementFormat.SpaceDashSpace, string customColon = null);

    string CleanTitle(string title);

    bool IsValidFileName(string fileName);

    bool IsValidPath(string path);
}
