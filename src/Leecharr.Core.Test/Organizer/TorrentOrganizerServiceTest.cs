// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.OrganizerTests;

public enum CollisionResolution
{
    Overwrite,
    Skip,
    RenameWithIndex,
    Fail,
}

public enum OrganizeStatus
{
    Planned,
    Success,
    Skipped,
    Failed,
}

public enum OrganizeOperation
{
    Move,
    Copy,
}

public class OrganizeFilePlan
{
    public string SourcePath { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public string DestinationFolder { get; set; } = string.Empty;

    public string DestinationFileName { get; set; } = string.Empty;

    public bool CollisionDetected { get; set; }

    public OrganizeStatus Status { get; set; }

    public bool IsDryRun { get; set; }

    public string Message { get; set; } = string.Empty;
}

public class TorrentOrganizerService
{
    private readonly IFileNameBuilder fileNameBuilder;
    private readonly IFileNameSanitizer fileNameSanitizer;
    private readonly IDiskProvider diskProvider;
    private readonly NamingConfig namingConfig;

    public TorrentOrganizerService(
        IFileNameBuilder fileNameBuilder,
        IFileNameSanitizer fileNameSanitizer,
        IDiskProvider diskProvider,
        NamingConfig namingConfig = null)
    {
        this.fileNameBuilder = fileNameBuilder ?? throw new ArgumentNullException(nameof(fileNameBuilder));
        this.fileNameSanitizer = fileNameSanitizer ?? throw new ArgumentNullException(nameof(fileNameSanitizer));
        this.diskProvider = diskProvider ?? throw new ArgumentNullException(nameof(diskProvider));
        this.namingConfig = namingConfig ?? new NamingConfig();
    }

    public OrganizeFilePlan OrganizeEpisode(
        string sourcePath,
        string destinationRoot,
        EpisodeNamingContext context,
        CollisionResolution collisionResolution = CollisionResolution.RenameWithIndex,
        bool dryRun = false,
        OrganizeOperation operation = OrganizeOperation.Move)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new ArgumentException("Destination root cannot be empty.", nameof(destinationRoot));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var seriesFolder = this.fileNameBuilder.BuildSeriesDirectory(context, namingConfig: this.namingConfig);
        var seasonFolder = this.fileNameBuilder.BuildSeasonDirectory(context, namingConfig: this.namingConfig);
        var fileName = this.fileNameBuilder.BuildFileName(context, namingConfig: this.namingConfig);

        var targetDir = Path.Combine(destinationRoot, seriesFolder, seasonFolder);
        targetDir = this.fileNameSanitizer.SanitizePath(targetDir, this.namingConfig.ColonReplacementFormat, this.namingConfig.CustomColonReplacementFormat);

        return this.ExecutePlan(sourcePath, targetDir, fileName, collisionResolution, dryRun, operation);
    }

    public OrganizeFilePlan OrganizeMovie(
        string sourcePath,
        string destinationRoot,
        MovieNamingContext context,
        CollisionResolution collisionResolution = CollisionResolution.RenameWithIndex,
        bool dryRun = false,
        OrganizeOperation operation = OrganizeOperation.Move)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be empty.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new ArgumentException("Destination root cannot be empty.", nameof(destinationRoot));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var movieFolder = this.fileNameBuilder.BuildMovieDirectory(context, namingConfig: this.namingConfig);
        var fileName = this.fileNameBuilder.BuildFileName(context, namingConfig: this.namingConfig);

        var targetDir = Path.Combine(destinationRoot, movieFolder);
        targetDir = this.fileNameSanitizer.SanitizePath(targetDir, this.namingConfig.ColonReplacementFormat, this.namingConfig.CustomColonReplacementFormat);

        return this.ExecutePlan(sourcePath, targetDir, fileName, collisionResolution, dryRun, operation);
    }

    public IReadOnlyList<OrganizeFilePlan> OrganizeBatch(
        IEnumerable<(string SourcePath, EpisodeNamingContext Context)> items,
        string destinationRoot,
        CollisionResolution collisionResolution = CollisionResolution.RenameWithIndex,
        bool dryRun = false)
    {
        var results = new List<OrganizeFilePlan>();
        foreach (var item in items)
        {
            var plan = this.OrganizeEpisode(item.SourcePath, destinationRoot, item.Context, collisionResolution, dryRun);
            results.Add(plan);
        }

        return results;
    }

    private OrganizeFilePlan ExecutePlan(
        string sourcePath,
        string targetDir,
        string fileName,
        CollisionResolution collisionResolution,
        bool dryRun,
        OrganizeOperation operation)
    {
        var targetPath = Path.Combine(targetDir, fileName);
        var collisionDetected = this.diskProvider.FileExists(targetPath);
        var finalPath = targetPath;
        var finalFileName = fileName;

        if (collisionDetected)
        {
            switch (collisionResolution)
            {
                case CollisionResolution.Skip:
                    return new OrganizeFilePlan
                    {
                        SourcePath = sourcePath,
                        DestinationPath = targetPath,
                        DestinationFolder = targetDir,
                        DestinationFileName = fileName,
                        CollisionDetected = true,
                        Status = OrganizeStatus.Skipped,
                        IsDryRun = dryRun,
                        Message = "Skipped because destination file already exists.",
                    };

                case CollisionResolution.Fail:
                    throw new IOException($"Destination file collision encountered: {targetPath}");

                case CollisionResolution.RenameWithIndex:
                    finalPath = this.ResolveIndexedCollisionPath(targetDir, fileName, out finalFileName);
                    break;

                case CollisionResolution.Overwrite:
                    // finalPath remains targetPath with overwrite enabled
                    break;
            }
        }

        if (dryRun)
        {
            return new OrganizeFilePlan
            {
                SourcePath = sourcePath,
                DestinationPath = finalPath,
                DestinationFolder = targetDir,
                DestinationFileName = finalFileName,
                CollisionDetected = collisionDetected,
                Status = OrganizeStatus.Planned,
                IsDryRun = true,
                Message = $"Planned {operation} to {finalPath}",
            };
        }

        this.diskProvider.EnsureFolder(targetDir);

        if (operation == OrganizeOperation.Move)
        {
            this.diskProvider.MoveFile(sourcePath, finalPath, overwrite: collisionResolution == CollisionResolution.Overwrite);
        }
        else
        {
            this.diskProvider.CopyFile(sourcePath, finalPath, overwrite: collisionResolution == CollisionResolution.Overwrite);
        }

        return new OrganizeFilePlan
        {
            SourcePath = sourcePath,
            DestinationPath = finalPath,
            DestinationFolder = targetDir,
            DestinationFileName = finalFileName,
            CollisionDetected = collisionDetected,
            Status = OrganizeStatus.Success,
            IsDryRun = false,
            Message = $"Successfully executed {operation} to {finalPath}",
        };
    }

    private string ResolveIndexedCollisionPath(string targetDir, string originalFileName, out string resolvedFileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var extension = Path.GetExtension(originalFileName);

        var index = 1;
        while (true)
        {
            var candidateName = $"{baseName} ({index}){extension}";
            var candidatePath = Path.Combine(targetDir, candidateName);

            if (!this.diskProvider.FileExists(candidatePath))
            {
                resolvedFileName = candidateName;
                return candidatePath;
            }

            index++;
        }
    }
}

[TestFixture]
public class TorrentOrganizerServiceTest
{
    private IFileNameBuilder fileNameBuilder = null!;
    private IFileNameSanitizer fileNameSanitizer = null!;
    private IDiskProvider diskProvider = null!;
    private NamingConfig namingConfig = null!;
    private TorrentOrganizerService organizerService = null!;

    [SetUp]
    public void SetUp()
    {
        this.fileNameSanitizer = new FileNameSanitizer();
        this.fileNameBuilder = new FileNameBuilder(this.fileNameSanitizer, new PathTruncator());
        this.diskProvider = Substitute.For<IDiskProvider>();
        this.namingConfig = new NamingConfig();

        this.organizerService = new TorrentOrganizerService(
            this.fileNameBuilder,
            this.fileNameSanitizer,
            this.diskProvider,
            this.namingConfig);
    }

    [Test]
    public void OrganizeEpisode_StandardFormatting_BuildsExpectedFileName()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Breaking Bad",
            SeasonNumber = 5,
            EpisodeNumbers = new List<int> { 14 },
            EpisodeTitles = new List<string> { "Ozymandias" },
            QualityFull = "WEBDL-1080p",
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/Breaking.Bad.S05E14.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("Breaking Bad - S05E14 - Ozymandias [WEBDL-1080p].mkv");
        plan.DestinationFolder.Should().Be("/media/tv/Breaking Bad/Season 5");
    }

    [TestCase(MultiEpisodeStyle.Extend, "Better Call Saul - S06E01-E02 - Wine and Roses.mkv")]
    [TestCase(MultiEpisodeStyle.Range, "Better Call Saul - S06E01-02 - Wine and Roses.mkv")]
    [TestCase(MultiEpisodeStyle.HyphenatedNumbers, "Better Call Saul - S06E01-E02 - Wine and Roses.mkv")]
    [TestCase(MultiEpisodeStyle.Scene, "Better Call Saul - S06E01.E02 - Wine and Roses.mkv")]
    public void OrganizeEpisode_MultiEpisodeStyles_FormatsFileNameAccordingly(
        MultiEpisodeStyle style,
        string expectedFileName)
    {
        this.namingConfig.MultiEpisodeStyle = style;
        this.namingConfig.StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {Episode CleanTitle}";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Better Call Saul",
            SeasonNumber = 6,
            EpisodeNumbers = new List<int> { 1, 2 },
            EpisodeTitles = new List<string> { "Wine and Roses" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/bcs.s06e01-02.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be(expectedFileName);
    }

    [Test]
    public void OrganizeEpisode_DailyEpisodeStyle_FormatsWithAirDate()
    {
        this.namingConfig.DailyEpisodeFormat = "{Series Title} - {Air-Date} - {Episode CleanTitle}";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "The Daily Show",
            SeasonNumber = 2024,
            EpisodeNumbers = new List<int>(),
            AirDate = new DateTime(2024, 10, 15),
            EpisodeTitles = new List<string> { "Jon Stewart Returns" },
            Extension = "mp4",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/daily.show.2024.10.15.mp4",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("The Daily Show - 2024-10-15 - Jon Stewart Returns.mp4");
    }

    [Test]
    public void OrganizeEpisode_AnimeStyle_FormatsWithAbsoluteEpisodeNumber()
    {
        this.namingConfig.AnimeEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - {absolute:000} - {Episode CleanTitle}";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Attack on Titan",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int>(),
            AbsoluteEpisodeNumbers = new List<int> { 25 },
            EpisodeTitles = new List<string> { "Wall" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/aot_25.mkv",
            "/media/anime",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("Attack on Titan - S01E00 - 025 - Wall.mkv");
    }

    [Test]
    public void OrganizeMovie_StandardFormatting_BuildsTitleYearEditionAndQuality()
    {
        this.namingConfig.StandardMovieFormat = "{Movie Title} ({Release Year}) {Edition Tags} [{Quality Full}]";

        var context = new MovieNamingContext
        {
            MovieTitle = "Blade Runner 2049",
            ReleaseYear = 2017,
            Edition = "[Director's Cut]",
            QualityFull = "Bluray-2160p Remux",
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeMovie(
            "/downloads/Blade.Runner.2049.2017.mkv",
            "/media/movies",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("Blade Runner 2049 (2017) [Director's Cut] [Bluray-2160p Remux].mkv");
        plan.DestinationFolder.Should().Be("/media/movies/Blade Runner 2049 (2017)");
    }

    [Test]
    public void TagExpansion_MediaInfoTags_ExpandsVideoAudioChannelsAndHdr()
    {
        this.namingConfig.StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} [{MediaInfo VideoCodec} {MediaInfo AudioCodec} {MediaInfo AudioChannels} {MediaInfo HdrFormat}]-{Release Group}";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "House of the Dragon",
            SeasonNumber = 2,
            EpisodeNumbers = new List<int> { 4 },
            EpisodeTitles = new List<string> { "A Dance of Dragons" },
            VideoCodec = "x265",
            AudioCodec = "TrueHD Atmos",
            AudioChannels = "7.1",
            HdrFormat = "Dolby Vision",
            ReleaseGroup = "FLUX",
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/hotd.s02e04.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("House of the Dragon - S02E04 [x265 TrueHD Atmos 7.1 Dolby Vision]-FLUX.mkv");
    }

    [Test]
    public void TagExpansion_MetadataIdentifiers_ExpandsImdbTmdbTvdbIds()
    {
        this.namingConfig.StandardMovieFormat = "{Movie Title} ({Release Year}) [imdbid-{ImdbId}] [tmdbid-{TmdbId}]";

        var context = new MovieNamingContext
        {
            MovieTitle = "The Dark Knight",
            ReleaseYear = 2008,
            ImdbId = "tt0468569",
            TmdbId = "155",
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeMovie(
            "/downloads/dark_knight.mkv",
            "/media/movies",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("The Dark Knight (2008) [imdbid-tt0468569] [tmdbid-155].mkv");
    }

    [Test]
    public void TagExpansion_CleanTitle_SanitizesPunctuationAndConvertsAmpersand()
    {
        this.namingConfig.StandardEpisodeFormat = "{Series CleanTitle} - S{season:00}E{episode:00} - {Episode CleanTitle}";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Rick & Morty: Season Quest!",
            SeasonNumber = 7,
            EpisodeNumbers = new List<int> { 5 },
            EpisodeTitles = new List<string> { "Unmortricken's Revenge?" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/rick.and.morty.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("Rick and Morty Season Quest - S07E05 - Unmortrickens Revenge.mkv");
    }

    [Test]
    public void TagExpansion_OriginalFilename_PreservesOriginalNameToken()
    {
        this.namingConfig.StandardEpisodeFormat = "{Series Title} - S{season:00}E{episode:00} - [{Original Filename}]";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Severance",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            OriginalFileName = "Severance.S01E01.Good.News.About.Hell.1080p.ATVP.mkv",
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/Severance.S01E01.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Should().Be("Severance - S01E01 - [Severance.S01E01.Good.News.About.Hell.1080p.ATVP].mkv");
    }

    [Test]
    public void DirectoryRestructuring_SpecialSeasonZero_MapsToSpecialsFolder()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Fargo",
            SeasonNumber = 0,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "Inside Season 1" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/fargo.special.mkv",
            "/library/series",
            context,
            dryRun: true);

        plan.DestinationFolder.Should().Be("/library/series/Fargo/Specials");
        plan.DestinationPath.Should().Be("/library/series/Fargo/Specials/Fargo - S00E01 - Inside Season 1.mkv");
    }

    [Test]
    public void DirectoryRestructuring_CustomFolderFormats_ExpandsSeriesAndSeasonFolders()
    {
        this.namingConfig.SeriesFolderFormat = "{Series Title} ({Release Year}) [tvdb-{TvdbId}]";
        this.namingConfig.SeasonFolderFormat = "Season {season:00}";

        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Succession",
            ReleaseYear = 2018,
            TvdbId = "338186",
            SeasonNumber = 4,
            EpisodeNumbers = new List<int> { 10 },
            EpisodeTitles = new List<string> { "With Open Eyes" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/succession.s04e10.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFolder.Should().Be("/media/tv/Succession (2018) [tvdb-338186]/Season 04");
    }

    [Test]
    public void DirectoryRestructuring_BatchOrganize_RestructuresMultipleEpisodesUnderCorrectFolders()
    {
        var items = new List<(string, EpisodeNamingContext)>
        {
            ("/downloads/ep1.mkv", new EpisodeNamingContext { SeriesTitle = "Dark", SeasonNumber = 1, EpisodeNumbers = new List<int> { 1 }, EpisodeTitles = new List<string> { "Secrets" }, Extension = "mkv" }),
            ("/downloads/ep2.mkv", new EpisodeNamingContext { SeriesTitle = "Dark", SeasonNumber = 2, EpisodeNumbers = new List<int> { 1 }, EpisodeTitles = new List<string> { "Beginnings and Endings" }, Extension = "mkv" }),
        };

        var plans = this.organizerService.OrganizeBatch(items, "/media/tv", dryRun: true);

        plans.Should().HaveCount(2);
        plans[0].DestinationFolder.Should().Be("/media/tv/Dark/Season 1");
        plans[1].DestinationFolder.Should().Be("/media/tv/Dark/Season 2");
    }

    [Test]
    public void CollisionResolution_WhenSkip_KeepsOriginalAndMarksSkipped()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Chernobyl",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "1:23:45" },
            Extension = "mkv",
        };

        var expectedTarget = "/media/tv/Chernobyl/Season 1/Chernobyl - S01E01 - 1-23-45.mkv";
        this.diskProvider.FileExists(expectedTarget).Returns(true);

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/chernobyl.s01e01.mkv",
            "/media/tv",
            context,
            collisionResolution: CollisionResolution.Skip,
            dryRun: false);

        plan.Status.Should().Be(OrganizeStatus.Skipped);
        plan.CollisionDetected.Should().BeTrue();
        plan.DestinationPath.Should().Be(expectedTarget);
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void CollisionResolution_WhenOverwrite_PerformsMoveWithOverwriteFlag()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Chernobyl",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "1:23:45" },
            Extension = "mkv",
        };

        var expectedTarget = "/media/tv/Chernobyl/Season 1/Chernobyl - S01E01 - 1-23-45.mkv";
        this.diskProvider.FileExists(expectedTarget).Returns(true);

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/chernobyl.s01e01.mkv",
            "/media/tv",
            context,
            collisionResolution: CollisionResolution.Overwrite,
            dryRun: false);

        plan.Status.Should().Be(OrganizeStatus.Success);
        plan.CollisionDetected.Should().BeTrue();
        this.diskProvider.Received(1).MoveFile("/downloads/chernobyl.s01e01.mkv", expectedTarget, true);
    }

    [Test]
    public void CollisionResolution_WhenRenameWithIndex_AppendsIncrementingNumber()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Chernobyl",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "1:23:45" },
            Extension = "mkv",
        };

        var originalTarget = "/media/tv/Chernobyl/Season 1/Chernobyl - S01E01 - 1-23-45.mkv";
        var indexedTarget1 = "/media/tv/Chernobyl/Season 1/Chernobyl - S01E01 - 1-23-45 (1).mkv";
        var indexedTarget2 = "/media/tv/Chernobyl/Season 1/Chernobyl - S01E01 - 1-23-45 (2).mkv";

        this.diskProvider.FileExists(originalTarget).Returns(true);
        this.diskProvider.FileExists(indexedTarget1).Returns(true);
        this.diskProvider.FileExists(indexedTarget2).Returns(false);

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/chernobyl.s01e01.mkv",
            "/media/tv",
            context,
            collisionResolution: CollisionResolution.RenameWithIndex,
            dryRun: false);

        plan.Status.Should().Be(OrganizeStatus.Success);
        plan.CollisionDetected.Should().BeTrue();
        plan.DestinationFileName.Should().Be("Chernobyl - S01E01 - 1-23-45 (2).mkv");
        plan.DestinationPath.Should().Be(indexedTarget2);
        this.diskProvider.Received(1).MoveFile("/downloads/chernobyl.s01e01.mkv", indexedTarget2, false);
    }

    [Test]
    public void CollisionResolution_WhenFail_ThrowsIOException()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Chernobyl",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "1:23:45" },
            Extension = "mkv",
        };

        var expectedTarget = "/media/tv/Chernobyl/Season 1/Chernobyl - S01E01 - 1-23-45.mkv";
        this.diskProvider.FileExists(expectedTarget).Returns(true);

        var act = () => this.organizerService.OrganizeEpisode(
            "/downloads/chernobyl.s01e01.mkv",
            "/media/tv",
            context,
            collisionResolution: CollisionResolution.Fail,
            dryRun: false);

        act.Should().Throw<IOException>()
            .WithMessage("*collision encountered*");
    }

    [Test]
    public void DryRunMode_WhenTrue_DoesNotCreateFoldersOrMoveFiles()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "The Wire",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "The Target" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/the.wire.s01e01.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.IsDryRun.Should().BeTrue();
        plan.Status.Should().Be(OrganizeStatus.Planned);
        plan.DestinationPath.Should().Be("/media/tv/The Wire/Season 1/The Wire - S01E01 - The Target.mkv");

        this.diskProvider.DidNotReceive().EnsureFolder(Arg.Any<string>());
        this.diskProvider.DidNotReceive().MoveFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
        this.diskProvider.DidNotReceive().CopyFile(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Test]
    public void DryRunMode_WhenFalse_ExecutesEnsureFolderAndMoveFile()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "The Wire",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "The Target" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/the.wire.s01e01.mkv",
            "/media/tv",
            context,
            dryRun: false);

        plan.IsDryRun.Should().BeFalse();
        plan.Status.Should().Be(OrganizeStatus.Success);

        this.diskProvider.Received(1).EnsureFolder("/media/tv/The Wire/Season 1");
        this.diskProvider.Received(1).MoveFile(
            "/downloads/the.wire.s01e01.mkv",
            "/media/tv/The Wire/Season 1/The Wire - S01E01 - The Target.mkv",
            false);
    }

    [Test]
    public void DryRunMode_WithCopyOperation_PerformsCopyFileWhenNotDryRun()
    {
        var context = new MovieNamingContext
        {
            MovieTitle = "Dune Part Two",
            ReleaseYear = 2024,
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeMovie(
            "/downloads/dune2.mkv",
            "/media/movies",
            context,
            dryRun: false,
            operation: OrganizeOperation.Copy);

        plan.Status.Should().Be(OrganizeStatus.Success);
        this.diskProvider.Received(1).CopyFile(
            "/downloads/dune2.mkv",
            "/media/movies/Dune Part Two (2024)/Dune Part Two (2024).mkv",
            false);
    }

    [TestCase("Star Wars: Episode IV: A New Hope", ColonReplacementFormat.SpaceDashSpace, "Star Wars - Episode IV - A New Hope")]
    [TestCase("Star Wars: Episode IV", ColonReplacementFormat.Dash, "Star Wars- Episode IV")]
    [TestCase("Star Wars: Episode IV", ColonReplacementFormat.Delete, "Star Wars Episode IV")]
    [TestCase("Star Wars: Episode IV", ColonReplacementFormat.SpaceDash, "Star Wars - Episode IV")]
    [TestCase("20:01: A Space Odyssey", ColonReplacementFormat.Smart, "20-01 - A Space Odyssey")]
    [TestCase("Star Wars: Episode IV", ColonReplacementFormat.Custom, "Star Wars_ Episode IV")]
    public void PathSanitization_ColonReplacement_SubstitutesAccordingToConfig(
        string rawTitle,
        ColonReplacementFormat format,
        string expectedTitle)
    {
        var customReplacement = format == ColonReplacementFormat.Custom ? "_" : string.Empty;
        var sanitized = this.fileNameSanitizer.SanitizeFileName(rawTitle + ".mkv", format, customReplacement);

        sanitized.Should().Be(expectedTitle + ".mkv");
    }

    [TestCase("Movie*Title?Name.mkv", "MovieTitleName.mkv")]
    [TestCase("Show<With>|Pipes\"AndQuotes.mkv", "ShowWithPipesAndQuotes.mkv")]
    [TestCase("Show\u0000Control\u001FChars.mkv", "ShowControlChars.mkv")]
    [TestCase("TrailingDotsAndSpaces...   .mkv", "TrailingDotsAndSpaces.mkv")]
    public void PathSanitization_IllegalCharacters_StripsForbiddenWindowsAndLinuxCharacters(
        string input,
        string expected)
    {
        var sanitized = this.fileNameSanitizer.SanitizeFileName(input);
        sanitized.Should().Be(expected);
    }

    [TestCase("CON.mkv", "_CON.mkv")]
    [TestCase("PRN.mkv", "_PRN.mkv")]
    [TestCase("AUX.mkv", "_AUX.mkv")]
    [TestCase("NUL.mkv", "_NUL.mkv")]
    [TestCase("COM1.mkv", "_COM1.mkv")]
    [TestCase("LPT9.mkv", "_LPT9.mkv")]
    public void PathSanitization_DosReservedNames_PrependsUnderscore(
        string input,
        string expected)
    {
        var sanitized = this.fileNameSanitizer.SanitizeFileName(input);
        sanitized.Should().Be(expected);
    }

    [Test]
    public void PathSanitization_DirectoryTraversal_NeutralizesDotDotSequences()
    {
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "../../../../../etc/passwd",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { "../../root_exploit" },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/exploit.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFolder.Should().NotContain("..");
        plan.DestinationFileName.Should().NotContain("..");
        plan.DestinationPath.Should().StartWith("/media/tv");
    }

    [Test]
    public void PathSanitization_LongFilename_TruncatesToSafeLengthPreservingExtension()
    {
        var longTitle = new string('A', 300);
        var context = new EpisodeNamingContext
        {
            SeriesTitle = "Show",
            SeasonNumber = 1,
            EpisodeNumbers = new List<int> { 1 },
            EpisodeTitles = new List<string> { longTitle },
            Extension = "mkv",
        };

        var plan = this.organizerService.OrganizeEpisode(
            "/downloads/long.mkv",
            "/media/tv",
            context,
            dryRun: true);

        plan.DestinationFileName.Length.Should().BeLessThanOrEqualTo(255);
        plan.DestinationFileName.Should().EndWith(".mkv");
    }

    [Test]
    public void PathSanitization_IsValidFileNameAndPath_AccuratelyValidates()
    {
        this.fileNameSanitizer.IsValidFileName("Valid_Show_S01E01.mkv").Should().BeTrue();
        this.fileNameSanitizer.IsValidFileName("Invalid:Colon.mkv").Should().BeFalse();
        this.fileNameSanitizer.IsValidFileName("CON.mkv").Should().BeFalse();
        this.fileNameSanitizer.IsValidFileName("..").Should().BeFalse();

        this.fileNameSanitizer.IsValidPath("/media/tv/Valid Show/Season 01/file.mkv").Should().BeTrue();
        this.fileNameSanitizer.IsValidPath("/media/tv/../invalid/file.mkv").Should().BeFalse();
    }
}
