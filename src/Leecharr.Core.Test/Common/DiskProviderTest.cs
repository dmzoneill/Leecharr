using System;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;

namespace Leecharr.Core.Test.Common;

[TestFixture]
public class DiskProviderTest
{
    private DiskProvider _diskProvider = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _diskProvider = new DiskProvider();
        _tempDir = Path.Combine(Path.GetTempPath(), "leecharr-disk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Test]
    public void FolderExists_WhenFolderExists_ReturnsTrue()
    {
        _diskProvider.FolderExists(_tempDir).Should().BeTrue();
    }

    [Test]
    public void FolderExists_WhenFolderDoesNotExist_ReturnsFalse()
    {
        _diskProvider.FolderExists(Path.Combine(_tempDir, "nonexistent")).Should().BeFalse();
    }

    [Test]
    public void FileExists_WhenFileExists_ReturnsTrue()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        File.WriteAllText(filePath, "sample content");

        _diskProvider.FileExists(filePath).Should().BeTrue();
    }

    [Test]
    public void EnsureFolder_CreatesFolder()
    {
        var targetFolder = Path.Combine(_tempDir, "subfolder", "nested");
        _diskProvider.EnsureFolder(targetFolder);

        Directory.Exists(targetFolder).Should().BeTrue();
    }

    [Test]
    public void WriteAllText_And_ReadAllText_Works()
    {
        var filePath = Path.Combine(_tempDir, "sample.txt");
        _diskProvider.WriteAllText(filePath, "Hello World");

        var content = _diskProvider.ReadAllText(filePath);
        content.Should().Be("Hello World");
    }

    [Test]
    public void DeleteFile_DeletesExistingFile()
    {
        var filePath = Path.Combine(_tempDir, "to_delete.txt");
        File.WriteAllText(filePath, "delete me");

        _diskProvider.DeleteFile(filePath);
        File.Exists(filePath).Should().BeFalse();
    }

    [Test]
    public void MoveFile_MovesFileToDestination()
    {
        var src = Path.Combine(_tempDir, "source.txt");
        var dest = Path.Combine(_tempDir, "destination.txt");
        File.WriteAllText(src, "payload");

        _diskProvider.MoveFile(src, dest, overwrite: true);

        File.Exists(src).Should().BeFalse();
        File.Exists(dest).Should().BeTrue();
        File.ReadAllText(dest).Should().Be("payload");
    }

    [Test]
    public void GetFileSize_ReturnsCorrectLength()
    {
        var filePath = Path.Combine(_tempDir, "size.txt");
        File.WriteAllBytes(filePath, new byte[1024]);

        var size = _diskProvider.GetFileSize(filePath);
        size.Should().Be(1024);
    }

    [Test]
    public void GetFiles_And_GetDirectories_ReturnsEntries()
    {
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "b");
        Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));

        var files = _diskProvider.GetFiles(_tempDir, recursive: false);
        var dirs = _diskProvider.GetDirectories(_tempDir);

        files.Should().HaveCount(2);
        dirs.Should().HaveCount(1);
    }
}
