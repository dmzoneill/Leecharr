// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class DownloadCompletionPathIntegrationTests : IntegrationTestBase
{
    private IStoragePathService storagePathService = null!;
    private IConfigService configService = null!;
    private ICategoryService categoryService = null!;
    private IDiskProvider diskProvider = null!;
    private string completedDir = null!;
    private string incompleteDir = null!;

    [SetUp]
    public void SetUp()
    {
        var services = GlobalSetup.Factory.Services;
        this.storagePathService = services.GetRequiredService<IStoragePathService>();
        this.configService = services.GetRequiredService<IConfigService>();
        this.categoryService = services.GetRequiredService<ICategoryService>();
        this.diskProvider = services.GetRequiredService<IDiskProvider>();

        this.completedDir = this.storagePathService.GetCompletedDirectory(null);
        this.incompleteDir = this.storagePathService.GetIncompleteDirectory();

        Directory.CreateDirectory(this.completedDir);
        Directory.CreateDirectory(this.incompleteDir);
    }

    [Test]
    public async Task SingleFileTorrent_SavePathReportsCompletedFolder_AndResolvesDirectlyOnFile()
    {
        const string hash = "5111111111111111111111111111111111111111";
        const string fileName = "StandaloneMovie.2024.1080p.mkv";
        const string category = "radarr";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={fileName}";

        // Create incomplete source file
        var incompleteFile = Path.Combine(this.incompleteDir, fileName);
        await File.WriteAllTextAsync(incompleteFile, "dummy video content");

        // Add torrent via Deluge RPC with category radarr
        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = category } },
            id = 101,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // Verify Deluge reports root completed download folder
            var statusRpc = new
            {
                method = "core.get_torrent_status",
                @params = new object[] { hash, new[] { "name", "save_path", "download_location", "label" } },
                id = 102,
            };
            var statusResp = await this.PostJsonAsync("/json", statusRpc);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(statusJson);
            var res = doc.RootElement.GetProperty("result");

            var savePath = res.GetProperty("save_path").GetString();
            savePath.Should().Be(this.completedDir);
            res.GetProperty("download_location").GetString().Should().Be(this.completedDir);
            res.GetProperty("label").GetString().Should().Be(category);

            // Move to completed directory (simulating download completion)
            var moved = this.storagePathService.MoveToCompleted(incompleteFile, category, fileName, out var finalDest);
            moved.Should().BeTrue();
            finalDest.Should().Be(Path.Combine(this.completedDir, fileName));

            // Verify Radarr OutputPath formula (save_path + "/" + name) resolves to existing file
            var radarrOutputPath = Path.Combine(savePath!, fileName);
            File.Exists(radarrOutputPath).Should().BeTrue("Radarr expects save_path + name to exist on disk as a file");
            Directory.Exists(radarrOutputPath).Should().BeFalse();
        }
        finally
        {
            await this.PostJsonAsync("/json", new { method = "core.remove_torrent", @params = new object[] { hash, true }, id = 103 });
            var expectedFile = Path.Combine(this.completedDir, fileName);
            if (File.Exists(expectedFile))
            {
                File.Delete(expectedFile);
            }
        }
    }

    [Test]
    public async Task FolderWithSingleFile_PreservesFolderStructure_AndRadarrResolvesFolder()
    {
        const string hash = "5222222222222222222222222222222222222222";
        const string folderName = "DarkPhoenix.2019.1080p.hevc"; // Release name, not ending in .mkv
        const string innerFileName = "DarkPhoenix.2019.BluRay.mkv";
        const string category = "radarr";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={folderName}";

        // Simulate file downloaded in incomplete directory
        var incompleteFile = Path.Combine(this.incompleteDir, innerFileName);
        await File.WriteAllTextAsync(incompleteFile, "dummy video content for folder single file");

        // Add torrent via Deluge RPC with category radarr
        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = category } },
            id = 201,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // Verify Deluge reports root completed download folder
            var statusRpc = new
            {
                method = "core.get_torrent_status",
                @params = new object[] { hash, new[] { "name", "save_path", "download_location" } },
                id = 202,
            };
            var statusResp = await this.PostJsonAsync("/json", statusRpc);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(statusJson);
            var res = doc.RootElement.GetProperty("result");

            var savePath = res.GetProperty("save_path").GetString();
            savePath.Should().Be(this.completedDir);

            var expectedFolder = Path.Combine(this.completedDir, folderName);

            // Move to completed directory (simulating download completion)
            var moved = this.storagePathService.MoveToCompleted(incompleteFile, category, folderName, out var finalDest);
            moved.Should().BeTrue();

            // Folder structure must be preserved under completed directory
            Directory.Exists(expectedFolder).Should().BeTrue("A folder with a single file must preserve the root directory on disk");

            // Radarr OutputPath formula (save_path + "/" + name) must resolve to an existing folder
            var radarrOutputPath = Path.Combine(savePath!, folderName);
            Directory.Exists(radarrOutputPath).Should().BeTrue("Radarr expects save_path + name to exist on disk as a folder");

            // Verify inner video file exists within the directory
            var innerFiles = Directory.GetFiles(expectedFolder);
            innerFiles.Should().NotBeEmpty();
            Path.GetExtension(innerFiles[0]).Should().Be(".mkv");
        }
        finally
        {
            await this.PostJsonAsync("/json", new { method = "core.remove_torrent", @params = new object[] { hash, true }, id = 203 });
            var expectedFolder = Path.Combine(this.completedDir, folderName);
            if (Directory.Exists(expectedFolder))
            {
                Directory.Delete(expectedFolder, true);
            }
        }
    }

    [Test]
    public async Task FolderWithMultipleFiles_PreservesFolder_AndRadarrResolvesFolder()
    {
        const string hash = "5333333333333333333333333333333333333333";
        const string folderName = "X-Men.Days.Of.Future.Past.2014.MULTi";
        const string category = "radarr";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={folderName}";

        // Simulate multi-file folder in incomplete directory
        var incompleteSourceFolder = Path.Combine(this.incompleteDir, folderName);
        Directory.CreateDirectory(incompleteSourceFolder);
        await File.WriteAllTextAsync(Path.Combine(incompleteSourceFolder, "movie.mp4"), "video content");
        await File.WriteAllTextAsync(Path.Combine(incompleteSourceFolder, "movie.nfo"), "nfo info");

        // Add torrent via Deluge RPC with category radarr
        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = category } },
            id = 301,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // Verify Deluge reports root completed download folder
            var statusRpc = new
            {
                method = "core.get_torrent_status",
                @params = new object[] { hash, new[] { "name", "save_path", "download_location" } },
                id = 302,
            };
            var statusResp = await this.PostJsonAsync("/json", statusRpc);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(statusJson);
            var res = doc.RootElement.GetProperty("result");

            var savePath = res.GetProperty("save_path").GetString();
            savePath.Should().Be(this.completedDir);

            // Move to completed directory (simulating download completion)
            var moved = this.storagePathService.MoveToCompleted(incompleteSourceFolder, category, folderName, out var finalDest);
            moved.Should().BeTrue();

            var expectedFolder = Path.Combine(this.completedDir, folderName);
            finalDest.Should().Be(expectedFolder);
            Directory.Exists(expectedFolder).Should().BeTrue();
            File.Exists(Path.Combine(expectedFolder, "movie.mp4")).Should().BeTrue();
            File.Exists(Path.Combine(expectedFolder, "movie.nfo")).Should().BeTrue();

            // Radarr OutputPath formula (save_path + "/" + name) must resolve to an existing folder
            var radarrOutputPath = Path.Combine(savePath!, folderName);
            Directory.Exists(radarrOutputPath).Should().BeTrue();
        }
        finally
        {
            await this.PostJsonAsync("/json", new { method = "core.remove_torrent", @params = new object[] { hash, true }, id = 303 });
            var expectedFolder = Path.Combine(this.completedDir, folderName);
            if (Directory.Exists(expectedFolder))
            {
                Directory.Delete(expectedFolder, true);
            }
        }
    }

    [Test]
    public async Task MultipleFoldersWithSubfolders_PreservesNestedDirectoryHierarchy()
    {
        const string hash = "5444444444444444444444444444444444444444";
        const string folderName = "Series.With.Nested.Subfolders.S01";
        const string category = "tv-sonarr";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={folderName}";

        // Simulate nested subfolder hierarchy in incomplete directory
        var incompleteSourceFolder = Path.Combine(this.incompleteDir, folderName);
        var subsDir = Path.Combine(incompleteSourceFolder, "Subs", "English");
        Directory.CreateDirectory(subsDir);
        await File.WriteAllTextAsync(Path.Combine(incompleteSourceFolder, "episode.mkv"), "video content");
        await File.WriteAllTextAsync(Path.Combine(subsDir, "1_English.srt"), "subtitle content");

        // Add torrent via Deluge RPC
        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = category } },
            id = 401,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // Verify Deluge reports root completed download folder
            var statusRpc = new
            {
                method = "core.get_torrent_status",
                @params = new object[] { hash, new[] { "name", "save_path", "download_location" } },
                id = 402,
            };
            var statusResp = await this.PostJsonAsync("/json", statusRpc);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(statusJson);
            var res = doc.RootElement.GetProperty("result");

            var savePath = res.GetProperty("save_path").GetString();
            savePath.Should().Be(this.completedDir);

            // Move to completed directory
            var moved = this.storagePathService.MoveToCompleted(incompleteSourceFolder, category, folderName, out var finalDest);
            moved.Should().BeTrue();

            var expectedFolder = Path.Combine(this.completedDir, folderName);
            finalDest.Should().Be(expectedFolder);
            Directory.Exists(expectedFolder).Should().BeTrue();
            File.Exists(Path.Combine(expectedFolder, "episode.mkv")).Should().BeTrue();
            Directory.Exists(Path.Combine(expectedFolder, "Subs", "English")).Should().BeTrue();
            File.Exists(Path.Combine(expectedFolder, "Subs", "English", "1_English.srt")).Should().BeTrue();

            // Sonarr OutputPath formula (save_path + "/" + name) must resolve to an existing folder
            var sonarrOutputPath = Path.Combine(savePath!, folderName);
            Directory.Exists(sonarrOutputPath).Should().BeTrue();
        }
        finally
        {
            await this.PostJsonAsync("/json", new { method = "core.remove_torrent", @params = new object[] { hash, true }, id = 403 });
            var expectedFolder = Path.Combine(this.completedDir, folderName);
            if (Directory.Exists(expectedFolder))
            {
                Directory.Delete(expectedFolder, true);
            }
        }
    }

    [Test]
    public async Task CategoryWithSubpathInDb_AlwaysNormalizesSavePathToCompletedDownloadDirectory()
    {
        const string hash = "5555555555555555555555555555555555555555";
        const string folderName = "CategorySubpathNormalizationTest";
        const string category = "custom-category";
        var magnet = $"magnet:?xt=urn:btih:{hash}&dn={folderName}";

        // Explicitly create category with SavePath pointing to /downloads/custom-category
        var subDir = Path.Combine(this.completedDir, category);
        this.categoryService.Add(new Category
        {
            Name = category,
            SavePath = subDir,
        });

        // Add torrent with this category
        var addRpc = new
        {
            method = "core.add_torrent_magnet",
            @params = new object[] { magnet, new { add_paused = true, label = category } },
            id = 501,
        };
        var addResp = await this.PostJsonAsync("/json", addRpc);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            // 1. Deluge core.get_torrent_status must report root completedDir, NOT completedDir/category
            var statusRpc = new
            {
                method = "core.get_torrent_status",
                @params = new object[] { hash, new[] { "name", "save_path", "download_location" } },
                id = 502,
            };
            var statusResp = await this.PostJsonAsync("/json", statusRpc);
            statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusJson = await statusResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(statusJson);
            var res = doc.RootElement.GetProperty("result");

            res.GetProperty("save_path").GetString().Should().Be(this.completedDir);
            res.GetProperty("download_location").GetString().Should().Be(this.completedDir);

            // 2. Deluge web.update_ui must report root completedDir
            var updateUiRpc = new
            {
                method = "web.update_ui",
                @params = new object[] { new[] { "name", "save_path" }, new { } },
                id = 503,
            };
            var updateUiResp = await this.PostJsonAsync("/json", updateUiRpc);
            updateUiResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updateUiJson = await updateUiResp.Content.ReadAsStringAsync();
            using var updateUiDoc = JsonDocument.Parse(updateUiJson);
            var torrents = updateUiDoc.RootElement.GetProperty("result").GetProperty("torrents");

            torrents.TryGetProperty(hash.ToLowerInvariant(), out var uiTorrent).Should().BeTrue();
            uiTorrent.GetProperty("save_path").GetString().Should().Be(this.completedDir);
        }
        finally
        {
            await this.PostJsonAsync("/json", new { method = "core.remove_torrent", @params = new object[] { hash, true }, id = 504 });
            var cat = this.categoryService.GetByName(category);
            if (cat != null)
            {
                this.categoryService.Delete(cat.Id);
            }
        }
    }
}
