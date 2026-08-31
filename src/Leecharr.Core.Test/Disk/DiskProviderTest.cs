// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;

namespace Leecharr.Core.Test.Disk;

[TestFixture]
public class DiskProviderTest
{
    private DiskProvider diskProvider = null!;
    private string tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        this.diskProvider = new DiskProvider();
        this.tempDir = Path.Combine(Path.GetTempPath(), "leecharr-disk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this.tempDir))
        {
            try
            {
                Directory.Delete(this.tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void GetAvailableSpace_WhenPathIsValid_ReturnsPositiveNumber()
    {
        var space = this.diskProvider.GetAvailableSpace(this.tempDir);

        space.Should().NotBeNull();
        space.Should().BeGreaterThan(0);
    }

    [Test]
    public void GetAvailableSpace_WhenPathIsRoot_ReturnsPositiveNumber()
    {
        var root = Path.GetPathRoot(this.tempDir) ?? (OperatingSystem.IsWindows() ? @"C:\" : "/");
        var space = this.diskProvider.GetAvailableSpace(root);

        space.Should().NotBeNull();
        space.Should().BeGreaterThan(0);
    }

    [Test]
    public void GetAvailableSpace_WhenSubfolderDoesNotExist_ReturnsDriveAvailableSpace()
    {
        var nonExistentSubfolder = Path.Combine(this.tempDir, "does", "not", "exist", "yet");
        var space = this.diskProvider.GetAvailableSpace(nonExistentSubfolder);

        space.Should().NotBeNull();
        space.Should().BeGreaterThan(0);
    }

    [Test]
    public void GetAvailableSpace_WhenPathIsInvalid_ReturnsNull()
    {
        var space = this.diskProvider.GetAvailableSpace("   ");

        space.Should().BeNull();
    }

    [Test]
    public void GetTotalSize_WhenPathIsValid_ReturnsPositiveNumber()
    {
        var totalSize = this.diskProvider.GetTotalSize(this.tempDir);

        totalSize.Should().NotBeNull();
        totalSize.Should().BeGreaterThan(0);
    }

    [Test]
    public void GetTotalSize_WhenPathIsInvalid_ReturnsNull()
    {
        var totalSize = this.diskProvider.GetTotalSize("   ");

        totalSize.Should().BeNull();
    }

    [Test]
    public void OpenWriteStream_WhenFileExists_TruncatesOnOverwrite()
    {
        var filePath = Path.Combine(this.tempDir, "truncate_test.bin");
        var initialData = new byte[100];
        Array.Fill(initialData, (byte)0xAA);
        File.WriteAllBytes(filePath, initialData);

        new FileInfo(filePath).Length.Should().Be(100);

        var newData = new byte[10];
        Array.Fill(newData, (byte)0xBB);
        using (var stream = this.diskProvider.OpenWriteStream(filePath))
        {
            stream.Write(newData, 0, newData.Length);
        }

        new FileInfo(filePath).Length.Should().Be(10);
        var readBack = File.ReadAllBytes(filePath);
        readBack.Should().Equal(newData);
    }

    [Test]
    public void OpenWriteStream_WhenParentDirectoryDoesNotExist_CreatesParentAndWrites()
    {
        var filePath = Path.Combine(this.tempDir, "nested", "deep", "file.dat");
        var data = new byte[] { 1, 2, 3, 4, 5 };

        using (var stream = this.diskProvider.OpenWriteStream(filePath))
        {
            stream.Write(data, 0, data.Length);
        }

        File.Exists(filePath).Should().BeTrue();
        new FileInfo(filePath).Length.Should().Be(5);
    }

    [Test]
    public void OpenReadStream_OpensAndReadsContent()
    {
        var filePath = Path.Combine(this.tempDir, "read_stream_test.txt");
        File.WriteAllText(filePath, "sample stream text");

        using var stream = this.diskProvider.OpenReadStream(filePath);
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        content.Should().Be("sample stream text");
    }

    [Test]
    public void FolderExists_WhenFolderExists_ReturnsTrue()
    {
        this.diskProvider.FolderExists(this.tempDir).Should().BeTrue();
    }

    [Test]
    public void FolderExists_WhenFolderDoesNotExist_ReturnsFalse()
    {
        this.diskProvider.FolderExists(Path.Combine(this.tempDir, "missing_dir")).Should().BeFalse();
    }

    [Test]
    public void FileExists_WhenFileExists_ReturnsTrue()
    {
        var filePath = Path.Combine(this.tempDir, "existing.txt");
        File.WriteAllText(filePath, "exists");

        this.diskProvider.FileExists(filePath).Should().BeTrue();
    }

    [Test]
    public void FileExists_WhenFileDoesNotExist_ReturnsFalse()
    {
        this.diskProvider.FileExists(Path.Combine(this.tempDir, "missing.txt")).Should().BeFalse();
    }

    [Test]
    public void DeleteFile_WhenFileExists_DeletesFile()
    {
        var filePath = Path.Combine(this.tempDir, "to_delete.txt");
        File.WriteAllText(filePath, "delete me");

        this.diskProvider.DeleteFile(filePath);

        File.Exists(filePath).Should().BeFalse();
    }

    [Test]
    public void DeleteFile_WhenFileDoesNotExist_DoesNotThrow()
    {
        var act = () => this.diskProvider.DeleteFile(Path.Combine(this.tempDir, "ghost.txt"));

        act.Should().NotThrow();
    }

    [Test]
    public void DeleteFolder_WhenRecursive_DeletesFolderAndChildren()
    {
        var subDir = Path.Combine(this.tempDir, "sub_dir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "child.txt"), "child data");

        this.diskProvider.DeleteFolder(subDir, recursive: true);

        Directory.Exists(subDir).Should().BeFalse();
    }

    [Test]
    public void DeleteFolder_WhenFolderDoesNotExist_DoesNotThrow()
    {
        var act = () => this.diskProvider.DeleteFolder(Path.Combine(this.tempDir, "missing_folder"), recursive: true);

        act.Should().NotThrow();
    }

    [Test]
    public void GetFileSize_WhenFileExists_ReturnsCorrectLength()
    {
        var filePath = Path.Combine(this.tempDir, "size_test.bin");
        var data = new byte[2048];
        File.WriteAllBytes(filePath, data);

        var size = this.diskProvider.GetFileSize(filePath);

        size.Should().Be(2048);
    }

    [Test]
    public void GetFileSize_WhenFileDoesNotExist_ReturnsZero()
    {
        var size = this.diskProvider.GetFileSize(Path.Combine(this.tempDir, "no_file.bin"));

        size.Should().Be(0);
    }

    [Test]
    public void GetFolderSize_WhenFolderHasNestedFiles_ReturnsSumOfFileSizes()
    {
        var subDir1 = Path.Combine(this.tempDir, "folder1");
        var subDir2 = Path.Combine(subDir1, "folder2");
        Directory.CreateDirectory(subDir2);

        File.WriteAllBytes(Path.Combine(this.tempDir, "file1.dat"), new byte[100]);
        File.WriteAllBytes(Path.Combine(subDir1, "file2.dat"), new byte[250]);
        File.WriteAllBytes(Path.Combine(subDir2, "file3.dat"), new byte[400]);

        var totalSize = this.diskProvider.GetFolderSize(this.tempDir);

        totalSize.Should().Be(750);
    }

    [Test]
    public void GetFolderSize_WhenFolderIsEmpty_ReturnsZero()
    {
        var emptyDir = Path.Combine(this.tempDir, "empty_dir");
        Directory.CreateDirectory(emptyDir);

        var size = this.diskProvider.GetFolderSize(emptyDir);

        size.Should().Be(0);
    }

    [Test]
    public void GetFolderSize_WhenFolderDoesNotExist_ReturnsZero()
    {
        var size = this.diskProvider.GetFolderSize(Path.Combine(this.tempDir, "does_not_exist"));

        size.Should().Be(0);
    }

    [Test]
    public void EnsureFolder_WhenFolderDoesNotExist_CreatesDirectoryStructure()
    {
        var targetFolder = Path.Combine(this.tempDir, "level1", "level2", "level3");
        this.diskProvider.EnsureFolder(targetFolder);

        Directory.Exists(targetFolder).Should().BeTrue();
    }

    [Test]
    public void EnsureFolder_WhenFolderAlreadyExists_DoesNotThrow()
    {
        var targetFolder = Path.Combine(this.tempDir, "existing_folder");
        Directory.CreateDirectory(targetFolder);

        var act = () => this.diskProvider.EnsureFolder(targetFolder);

        act.Should().NotThrow();
        Directory.Exists(targetFolder).Should().BeTrue();
    }

    [Test]
    public void CreateFolder_CreatesDirectory()
    {
        var newFolder = Path.Combine(this.tempDir, "created_dir");
        this.diskProvider.CreateFolder(newFolder);

        Directory.Exists(newFolder).Should().BeTrue();
    }

    [Test]
    public void FolderWritable_WhenFolderIsWritable_ReturnsTrue()
    {
        this.diskProvider.FolderWritable(this.tempDir).Should().BeTrue();
    }

    [Test]
    public void FolderWritable_WhenFolderIsInvalid_ReturnsFalse()
    {
        this.diskProvider.FolderWritable(Path.Combine(this.tempDir, "non_existent_path_no_create", "sub")).Should().BeFalse();
    }

    [Test]
    public void FolderEmpty_WhenFolderHasNoEntries_ReturnsTrue()
    {
        var emptyFolder = Path.Combine(this.tempDir, "empty_test");
        Directory.CreateDirectory(emptyFolder);

        this.diskProvider.FolderEmpty(emptyFolder).Should().BeTrue();
    }

    [Test]
    public void FolderEmpty_WhenFolderDoesNotExist_ReturnsTrue()
    {
        this.diskProvider.FolderEmpty(Path.Combine(this.tempDir, "ghost_dir")).Should().BeTrue();
    }

    [Test]
    public void FolderEmpty_WhenFolderContainsFiles_ReturnsFalse()
    {
        var folder = Path.Combine(this.tempDir, "non_empty_folder");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "data.txt"), "data");

        this.diskProvider.FolderEmpty(folder).Should().BeFalse();
    }

    [Test]
    public void FolderEmpty_WhenFolderContainsSubdirectories_ReturnsFalse()
    {
        var folder = Path.Combine(this.tempDir, "dir_with_subdir");
        Directory.CreateDirectory(Path.Combine(folder, "child_dir"));

        this.diskProvider.FolderEmpty(folder).Should().BeFalse();
    }

    [Test]
    public void GetFiles_WhenNonRecursive_ReturnsOnlyTopLevelFiles()
    {
        File.WriteAllText(Path.Combine(this.tempDir, "top1.txt"), "1");
        File.WriteAllText(Path.Combine(this.tempDir, "top2.txt"), "2");

        var subDir = Path.Combine(this.tempDir, "nested_dir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "3");

        var topFiles = this.diskProvider.GetFiles(this.tempDir, recursive: false);

        topFiles.Should().HaveCount(2);
        topFiles.Should().NotContain(Path.Combine(subDir, "nested.txt"));
    }

    [Test]
    public void GetFiles_WhenRecursive_ReturnsAllNestedFiles()
    {
        File.WriteAllText(Path.Combine(this.tempDir, "top.txt"), "1");

        var subDir = Path.Combine(this.tempDir, "nested_dir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "2");

        var deepDir = Path.Combine(subDir, "deep");
        Directory.CreateDirectory(deepDir);
        File.WriteAllText(Path.Combine(deepDir, "deep.txt"), "3");

        var allFiles = this.diskProvider.GetFiles(this.tempDir, recursive: true);

        allFiles.Should().HaveCount(3);
    }

    [Test]
    public void GetDirectories_ReturnsSubdirectories()
    {
        Directory.CreateDirectory(Path.Combine(this.tempDir, "sub_a"));
        Directory.CreateDirectory(Path.Combine(this.tempDir, "sub_b"));

        var dirs = this.diskProvider.GetDirectories(this.tempDir);

        dirs.Should().HaveCount(2);
    }

    [Test]
    public void WriteAllText_And_ReadAllText_CreatesParentDirAndPreservesContent()
    {
        var filePath = Path.Combine(this.tempDir, "deep", "nested", "text_file.txt");
        var content = "Testing WriteAllText with auto parent directory creation";

        this.diskProvider.WriteAllText(filePath, content);
        var readContent = this.diskProvider.ReadAllText(filePath);

        readContent.Should().Be(content);
    }

    [Test]
    public void CopyFile_CopiesFileAndCreatesTargetDirectory()
    {
        var src = Path.Combine(this.tempDir, "source.txt");
        var dest = Path.Combine(this.tempDir, "target_dir", "dest.txt");
        File.WriteAllText(src, "copy payload");

        this.diskProvider.CopyFile(src, dest, overwrite: true);

        File.Exists(src).Should().BeTrue();
        File.Exists(dest).Should().BeTrue();
        File.ReadAllText(dest).Should().Be("copy payload");
    }

    [Test]
    public void MoveFile_MovesFileAndCreatesTargetDirectory()
    {
        var src = Path.Combine(this.tempDir, "mv_source.txt");
        var dest = Path.Combine(this.tempDir, "move_dir", "mv_dest.txt");
        File.WriteAllText(src, "move payload");

        this.diskProvider.MoveFile(src, dest, overwrite: true);

        File.Exists(src).Should().BeFalse();
        File.Exists(dest).Should().BeTrue();
        File.ReadAllText(dest).Should().Be("move payload");
    }

    [Test]
    public void MoveFolder_MovesEntireDirectoryStructure()
    {
        var srcDir = Path.Combine(this.tempDir, "src_folder");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "item.txt"), "item data");

        var destDir = Path.Combine(this.tempDir, "dest_parent", "dest_folder");

        this.diskProvider.MoveFolder(srcDir, destDir);

        Directory.Exists(srcDir).Should().BeFalse();
        Directory.Exists(destDir).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "item.txt")).Should().BeTrue();
    }

    [Test]
    public void Timestamps_ReturnsValidDateTimes()
    {
        var testFile = Path.Combine(this.tempDir, "time_test.txt");
        File.WriteAllText(testFile, "time content");

        var folderCreated = this.diskProvider.FolderGetCreationTime(this.tempDir);
        var folderModified = this.diskProvider.FolderGetLastWrite(this.tempDir);
        var fileModified = this.diskProvider.FileGetLastWrite(testFile);

        folderCreated.Should().BeBefore(DateTime.Now.AddMinutes(1));
        folderModified.Should().BeBefore(DateTime.Now.AddMinutes(1));
        fileModified.Should().BeBefore(DateTime.Now.AddMinutes(1));
    }
}
