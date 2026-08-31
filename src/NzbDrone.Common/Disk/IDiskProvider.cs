// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;

namespace NzbDrone.Common.Disk;

public interface IDiskProvider
{
    long? GetAvailableSpace(string path);

    long? GetTotalSize(string path);

    DateTime FolderGetCreationTime(string path);

    DateTime FolderGetLastWrite(string path);

    DateTime FileGetLastWrite(string path);

    void EnsureFolder(string path);

    bool FolderExists(string path);

    bool FileExists(string path);

    bool FolderWritable(string path);

    bool FolderEmpty(string path);

    IEnumerable<string> GetDirectories(string path);

    IEnumerable<string> GetFiles(string path, bool recursive);

    long GetFolderSize(string path);

    long GetFileSize(string path);

    void CreateFolder(string path);

    void DeleteFile(string path);

    void CopyFile(string source, string destination, bool overwrite = false);

    void MoveFile(string source, string destination, bool overwrite = false);

    void MoveFolder(string source, string destination);

    void DeleteFolder(string path, bool recursive);

    string ReadAllText(string filePath);

    void WriteAllText(string filename, string contents);

    FileStream OpenReadStream(string path);

    FileStream OpenWriteStream(string path);
}
