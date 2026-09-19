// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Categories;

[TestFixture]
public class CategoryServiceTest
{
    private ICategoryRepository repository = null!;
    private IEventAggregator eventAggregator = null!;
    private ITorrentRepository torrentRepository = null!;
    private CategoryService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<ICategoryRepository>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.service = new CategoryService(this.repository, this.eventAggregator, this.torrentRepository);
    }

    [Test]
    public void GetAll_ReturnsSortedCategories()
    {
        var list = new List<Category>
        {
            new() { Id = 1, Name = "tv", SavePath = "/downloads/tv" },
            new() { Id = 2, Name = "anime", SavePath = "/downloads/anime" },
            new() { Id = 3, Name = "movies", SavePath = "/downloads/movies" },
        };

        this.repository.All().Returns(list);

        var result = this.service.GetAll().ToList();

        result.Should().HaveCount(3);
        result[0].Name.Should().Be("anime");
        result[1].Name.Should().Be("movies");
        result[2].Name.Should().Be("tv");
    }

    [Test]
    public void GetByName_WhenNameIsEmpty_ReturnsDefaultCategory()
    {
        var defaultCategory = new Category { Id = 1, Name = "default", SavePath = "/downloads/default", IsDefault = true };
        this.repository.GetDefault().Returns(defaultCategory);

        var result = this.service.GetByName(string.Empty);

        result.Should().NotBeNull();
        result.Name.Should().Be("default");
    }

    [Test]
    public void GetByName_WhenNameSpecified_ReturnsMatchingCategory()
    {
        var category = new Category { Id = 2, Name = "tv", SavePath = "/downloads/tv" };
        this.repository.GetByName("tv").Returns(category);

        var result = this.service.GetByName("tv");

        result.Should().NotBeNull();
        result.Name.Should().Be("tv");
        result.SavePath.Should().Be("/downloads/tv");
    }

    [Test]
    public void Add_InsertsCategoryAndPublishesEvent()
    {
        var category = new Category { Name = "music", SavePath = "/downloads/music" };
        this.repository.Insert(category).Returns(new Category { Id = 10, Name = "music", SavePath = "/downloads/music" });

        var inserted = this.service.Add(category);

        inserted.Id.Should().Be(10);
        this.repository.Received(1).Insert(category);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category.Id == 10));
    }

    [Test]
    public void Add_WhenCategoryNull_ThrowsArgumentNullException()
    {
        Action act = () => this.service.Add(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Update_UpdatesCategoryAndPublishesEvent()
    {
        var category = new Category { Id = 5, Name = "movies", SavePath = "/downloads/movies-new" };
        this.repository.Update(category).Returns(category);

        var updated = this.service.Update(category);

        updated.SavePath.Should().Be("/downloads/movies-new");
        this.repository.Received(1).Update(category);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category.Id == 5));
    }

    [Test]
    public void Delete_WhenCategoryIsDefault_PreventsDeletion()
    {
        var category = new Category { Id = 5, Name = "default-cat", IsDefault = true };
        this.repository.Get(5).Returns(category);

        this.service.Delete(5);

        this.repository.DidNotReceive().Delete(Arg.Any<int>());
        this.eventAggregator.DidNotReceive().PublishEvent(Arg.Any<CategoryDeletedEvent>());
    }

    [Test]
    public void Delete_WhenDefaultCategoryExists_ReassignsAffectedTorrentsToDefaultCategory()
    {
        var category = new Category { Id = 5, Name = "movies", IsDefault = false };
        this.repository.Get(5).Returns(category);
        this.repository.GetDefault().Returns(new Category { Id = 1, Name = "general", IsDefault = true });

        var torrent1 = new Torrent { Id = 1, Name = "Movie1", Category = "movies" };
        var torrent2 = new Torrent { Id = 2, Name = "Movie2", Category = "movies" };
        this.torrentRepository.GetByCategory("movies").Returns(new List<Torrent> { torrent1, torrent2 });

        this.service.Delete(5);

        torrent1.Category.Should().Be("general");
        torrent2.Category.Should().Be("general");
        this.torrentRepository.Received(1).Update(torrent1);
        this.torrentRepository.Received(1).Update(torrent2);
        this.repository.Received(1).Delete(5);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryDeletedEvent>(e => e.CategoryId == 5 && e.CategoryName == "movies"));
    }

    [Test]
    public void Delete_ClearsCategoryOnAffectedTorrentsPublishesEventAndDeletes()
    {
        var category = new Category { Id = 5, Name = "movies" };
        this.repository.Get(5).Returns(category);

        var torrent1 = new Torrent { Id = 1, Name = "Movie1", Category = "movies" };
        var torrent2 = new Torrent { Id = 2, Name = "Movie2", Category = "movies" };
        this.torrentRepository.GetByCategory("movies").Returns(new List<Torrent> { torrent1, torrent2 });

        this.service.Delete(5);

        torrent1.Category.Should().BeEmpty();
        torrent2.Category.Should().BeEmpty();
        this.torrentRepository.Received(1).Update(torrent1);
        this.torrentRepository.Received(1).Update(torrent2);
        this.repository.Received(1).Delete(5);
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryDeletedEvent>(e => e.CategoryId == 5 && e.CategoryName == "movies"));
    }

    [Test]
    public void Add_WhenIsDefault_ClearsExistingDefaultCategories()
    {
        var existingDefault = new Category { Id = 1, Name = "old-default", IsDefault = true };
        this.repository.All().Returns(new List<Category> { existingDefault });

        var newCategory = new Category { Id = 2, Name = "new-default", IsDefault = true };
        this.repository.Insert(newCategory).Returns(newCategory);

        this.service.Add(newCategory);

        existingDefault.IsDefault.Should().BeFalse();
        this.repository.Received(1).Update(existingDefault);
        this.repository.Received(1).Insert(newCategory);
    }

    [Test]
    public void Update_WhenIsDefault_ClearsOtherDefaultCategories()
    {
        var otherDefault = new Category { Id = 1, Name = "old-default", IsDefault = true };
        var categoryToUpdate = new Category { Id = 2, Name = "updated", IsDefault = true };
        this.repository.All().Returns(new List<Category> { otherDefault, categoryToUpdate });
        this.repository.Update(categoryToUpdate).Returns(categoryToUpdate);

        this.service.Update(categoryToUpdate);

        otherDefault.IsDefault.Should().BeFalse();
        this.repository.Received(1).Update(otherDefault);
        this.repository.Received(1).Update(categoryToUpdate);
    }

    [Test]
    public void GetSavePathForCategory_WhenCategoryExists_ReturnsCategorySavePath()
    {
        this.repository.GetByName("tv").Returns(new Category { Name = "tv", SavePath = "/custom/tv/path" });

        var path = this.service.GetSavePathForCategory("tv");

        path.Should().Be("/custom/tv/path");
    }

    [Test]
    public void GetSavePathForCategory_WhenCategoryNotFound_ReturnsDefaultCategoryPath()
    {
        this.repository.GetByName("unknown").Returns((Category)null!);
        this.repository.GetDefault().Returns(new Category { Name = "default", SavePath = "/default/path", IsDefault = true });

        var path = this.service.GetSavePathForCategory("unknown", "/fallback/path");

        path.Should().Be("/default/path");
    }

    [Test]
    public void GetSavePathForCategory_WhenNoDefaultFound_ReturnsFallbackDefaultPath()
    {
        this.repository.GetByName("unknown").Returns((Category)null!);
        this.repository.GetDefault().Returns((Category)null!);

        var path = this.service.GetSavePathForCategory("unknown", "/fallback/path");

        path.Should().Be("/fallback/path");
    }

    [Test]
    public void Update_WhenCategoryNameChanges_UpdatesReferencingTorrents()
    {
        var existing = new Category { Id = 5, Name = "tv", SavePath = "/downloads/tv" };
        var updated = new Category { Id = 5, Name = "television", SavePath = "/downloads/television" };
        this.repository.Get(5).Returns(existing);
        this.repository.Update(updated).Returns(updated);

        var torrent1 = new Torrent { Id = 1, Name = "Show1", Category = "tv" };
        var torrent2 = new Torrent { Id = 2, Name = "Show2", Category = "tv" };
        this.torrentRepository.GetByCategory("tv").Returns(new List<Torrent> { torrent1, torrent2 });

        this.service.Update(updated);

        torrent1.Category.Should().Be("television");
        torrent2.Category.Should().Be("television");
        this.torrentRepository.Received(1).Update(torrent1);
        this.torrentRepository.Received(1).Update(torrent2);
    }

    [Test]
    public void Add_WhenCategoryNameEmptyOrWhitespace_ThrowsArgumentException()
    {
        var category = new Category { Name = "   ", SavePath = "/downloads" };
        Action act = () => this.service.Add(category);
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Test]
    public void Add_WhenCategoryNegativeLimits_ThrowsArgumentException()
    {
        var category = new Category { Name = "test", DefaultUploadLimit = -10 };
        Action act = () => this.service.Add(category);
        act.Should().Throw<ArgumentException>().WithMessage("*non-negative*");
    }

    [Test]
    public void Add_TrimsCategoryNameAndSavePath()
    {
        var category = new Category { Name = "  movies  ", SavePath = "  /downloads/movies  " };
        this.repository.Insert(Arg.Any<Category>()).Returns(ci => ci.Arg<Category>());

        var inserted = this.service.Add(category);

        inserted.Name.Should().Be("movies");
        inserted.SavePath.Should().Be("/downloads/movies");
    }

    [Test]
    public void Update_WhenCategoryNameEmpty_ThrowsArgumentException()
    {
        var category = new Category { Id = 1, Name = string.Empty, SavePath = "/downloads" };
        Action act = () => this.service.Update(category);
        act.Should().Throw<ArgumentException>().WithMessage("*name*");
    }

    [Test]
    public void Update_WhenCategoryNegativeLimits_ThrowsArgumentException()
    {
        var category = new Category { Id = 1, Name = "valid", TargetRatio = -1.0 };
        Action act = () => this.service.Update(category);
        act.Should().Throw<ArgumentException>().WithMessage("*non-negative*");
    }

    [Test]
    public void Add_WhenSavePathContainsNullByte_ThrowsArgumentException()
    {
        var category = new Category { Name = "test", SavePath = "/downloads/\0bad" };
        Action act = () => this.service.Add(category);
        act.Should().Throw<ArgumentException>().WithMessage("*invalid*");
    }

    [Test]
    public void Add_WhenSavePathNotWritable_ThrowsInvalidOperationException()
    {
        var diskProvider = Substitute.For<NzbDrone.Common.Disk.IDiskProvider>();
        diskProvider.FolderExists("/restricted").Returns(true);
        diskProvider.FolderWritable("/restricted").Returns(false);

        var serviceWithDisk = new CategoryService(this.repository, this.eventAggregator, this.torrentRepository, diskProvider);
        var category = new Category { Name = "restricted", SavePath = "/restricted" };

        Action act = () => serviceWithDisk.Add(category);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not writable*");
    }

    [Test]
    public void Add_WhenCategoryAlreadyExists_UpdatesSavePathAndReturnsExistingWithoutInsert()
    {
        var existing = new Category { Id = 3, Name = "movies", SavePath = "/downloads/movies-old" };
        this.repository.GetByName("movies").Returns(existing);

        var newCategory = new Category { Name = "movies", SavePath = "/downloads/movies-new" };
        var result = this.service.Add(newCategory);

        result.Should().BeSameAs(existing);
        result.SavePath.Should().Be("/downloads/movies-new");
        this.repository.Received(1).Update(existing);
        this.repository.DidNotReceive().Insert(Arg.Any<Category>());
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category.Id == 3));
    }

    [Test]
    public void Add_WhenCategoryAlreadyExistsWithDifferentCase_UpdatesSavePathAndReturnsExisting()
    {
        var existing = new Category { Id = 4, Name = "tv", SavePath = "/downloads/tv-old" };
        this.repository.GetByName("TV").Returns(existing);

        var newCategory = new Category { Name = "TV", SavePath = "/downloads/tv-new" };
        var result = this.service.Add(newCategory);

        result.Should().BeSameAs(existing);
        result.SavePath.Should().Be("/downloads/tv-new");
        this.repository.Received(1).Update(existing);
        this.repository.DidNotReceive().Insert(Arg.Any<Category>());
    }

    [Test]
    public void Add_WhenCategoryAlreadyExistsAndSavePathEmpty_PreservesExistingSavePathAndDoesNotInsert()
    {
        var existing = new Category { Id = 5, Name = "music", SavePath = "/downloads/music" };
        this.repository.GetByName("music").Returns(existing);

        var newCategory = new Category { Name = "music", SavePath = string.Empty };
        var result = this.service.Add(newCategory);

        result.Should().BeSameAs(existing);
        result.SavePath.Should().Be("/downloads/music");
        this.repository.DidNotReceive().Update(Arg.Any<Category>());
        this.repository.DidNotReceive().Insert(Arg.Any<Category>());
    }

    [TestCase("/movies///uhd/", "movies/uhd")]
    [TestCase("\\movies\\\\uhd\\", "movies/uhd")]
    [TestCase("  / movies / uhd /  ", "movies/uhd")]
    [TestCase("movies", "movies")]
    [TestCase("///", "")]
    [TestCase("", "")]
    [TestCase(null, "")]
    public void NormalizeCategoryName_NormalizesSlashesAndWhitespace(string input, string expected)
    {
        var result = CategoryService.NormalizeCategoryName(input);
        result.Should().Be(expected);
    }

    [Test]
    public void Add_WithUnnormalizedName_NormalizesCategoryName()
    {
        var category = new Category { Name = "/movies///uhd/", SavePath = "/downloads/movies/uhd" };
        this.repository.Insert(Arg.Any<Category>()).Returns(callInfo => callInfo.Arg<Category>());

        var inserted = this.service.Add(category);

        inserted.Name.Should().Be("movies/uhd");
    }

    [Test]
    public void GetSavePathForCategory_WithSubcategoryAndEmptySavePath_InheritsParentSavePath()
    {
        var parentCat = new Category { Id = 1, Name = "movies", SavePath = "/downloads/movies" };
        var childCat = new Category { Id = 2, Name = "movies/uhd", SavePath = string.Empty };

        this.repository.GetByName("movies/uhd").Returns(childCat);
        this.repository.GetByName("movies").Returns(parentCat);

        var path = this.service.GetSavePathForCategory("movies/uhd");

        path.Should().Be(Path.Combine("/downloads/movies", "uhd"));
    }

    [Test]
    public void GetSavePathForCategory_WithMultiLevelSubcategory_InheritsAncestorSavePath()
    {
        var mediaCat = new Category { Id = 1, Name = "media", SavePath = "/storage/media" };
        var moviesCat = new Category { Id = 2, Name = "media/movies", SavePath = string.Empty };
        var uhdCat = new Category { Id = 3, Name = "media/movies/uhd", SavePath = string.Empty };

        this.repository.GetByName("media/movies/uhd").Returns(uhdCat);
        this.repository.GetByName("media/movies").Returns(moviesCat);
        this.repository.GetByName("media").Returns(mediaCat);

        var path = this.service.GetSavePathForCategory("media/movies/uhd");

        var expected = Path.Combine(Path.Combine("/storage/media", "movies"), "uhd");
        path.Should().Be(expected);
    }

    [Test]
    public void GetSavePathForCategory_WithBackslashes_NormalizesAndInherits()
    {
        var parentCat = new Category { Id = 1, Name = "movies", SavePath = "/downloads/movies" };
        this.repository.GetByName("movies/uhd").Returns(new Category { Name = "movies/uhd", SavePath = string.Empty });
        this.repository.GetByName("movies").Returns(parentCat);

        var path = this.service.GetSavePathForCategory("movies\\uhd");

        path.Should().Be(Path.Combine("/downloads/movies", "uhd"));
    }

    [Test]
    public void GetSavePathForCategory_WithExplicitChildSavePath_DoesNotInheritParentSavePath()
    {
        var parentCat = new Category { Id = 1, Name = "movies", SavePath = "/downloads/movies" };
        var childCat = new Category { Id = 2, Name = "movies/uhd", SavePath = "/fast/uhd" };

        this.repository.GetByName("movies/uhd").Returns(childCat);
        this.repository.GetByName("movies").Returns(parentCat);

        var path = this.service.GetSavePathForCategory("movies/uhd");

        path.Should().Be("/fast/uhd");
    }

    [Test]
    public void Update_WhenParentCategoryRenamed_CascadesNewNameToSubcategoriesAndTorrents()
    {
        var existingParent = new Category { Id = 1, Name = "movies", SavePath = "/downloads/movies" };
        var updatedParent = new Category { Id = 1, Name = "films", SavePath = "/downloads/movies" };

        var child1 = new Category { Id = 2, Name = "movies/uhd", SavePath = string.Empty };
        var child2 = new Category { Id = 3, Name = "movies/uhd/remux", SavePath = string.Empty };
        var other = new Category { Id = 4, Name = "other", SavePath = string.Empty };

        this.repository.Get(1).Returns(existingParent);
        this.repository.Update(updatedParent).Returns(updatedParent);
        this.repository.All().Returns(new List<Category> { updatedParent, child1, child2, other });

        var torrentParent = new Torrent { Id = 10, Name = "Movie1", Category = "movies" };
        var torrentChild1 = new Torrent { Id = 11, Name = "Movie2", Category = "movies/uhd" };
        var torrentChild2 = new Torrent { Id = 12, Name = "Movie3", Category = "movies/uhd/remux" };

        this.torrentRepository.GetByCategory("movies").Returns(new List<Torrent> { torrentParent });
        this.torrentRepository.GetByCategory("movies/uhd").Returns(new List<Torrent> { torrentChild1 });
        this.torrentRepository.GetByCategory("movies/uhd/remux").Returns(new List<Torrent> { torrentChild2 });

        this.service.Update(updatedParent);

        torrentParent.Category.Should().Be("films");
        torrentChild1.Category.Should().Be("films/uhd");
        torrentChild2.Category.Should().Be("films/uhd/remux");

        this.torrentRepository.Received(1).Update(torrentParent);
        this.torrentRepository.Received(1).Update(torrentChild1);
        this.torrentRepository.Received(1).Update(torrentChild2);

        child1.Name.Should().Be("films/uhd");
        child2.Name.Should().Be("films/uhd/remux");
        other.Name.Should().Be("other");

        this.repository.Received(1).Update(child1);
        this.repository.Received(1).Update(child2);
        this.repository.DidNotReceive().Update(other);

        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category.Name == "films/uhd"));
        this.eventAggregator.Received(1).PublishEvent(Arg.Is<CategoryUpdatedEvent>(e => e.Category.Name == "films/uhd/remux"));
    }

    [Test]
    public void Delete_WhenParentCategoryDeleted_CascadesDeletionToSubcategories()
    {
        var parent = new Category { Id = 1, Name = "movies" };
        var child1 = new Category { Id = 2, Name = "movies/uhd" };
        var child2 = new Category { Id = 3, Name = "movies/uhd/remux" };
        var other = new Category { Id = 4, Name = "music" };

        this.repository.Get(1).Returns(parent);
        this.repository.Get(2).Returns(child1);
        this.repository.Get(3).Returns(child2);
        this.repository.Get(4).Returns(other);

        this.repository.All().Returns(new List<Category> { parent, child1, child2, other });

        var torrentChild = new Torrent { Id = 20, Name = "UhdTorrent", Category = "movies/uhd" };
        this.torrentRepository.GetByCategory("movies/uhd").Returns(new List<Torrent> { torrentChild });

        this.service.Delete(1);

        this.repository.Received(1).Delete(1);
        this.repository.Received(1).Delete(2);
        this.repository.Received(1).Delete(3);
        this.repository.DidNotReceive().Delete(4);

        torrentChild.Category.Should().BeEmpty();
        this.torrentRepository.Received(1).Update(torrentChild);
    }

    [TestCase("downloads/movies")]
    [TestCase("./downloads")]
    [TestCase("../movies")]
    public void Add_WhenSavePathIsNotRooted_ThrowsArgumentException(string relativePath)
    {
        var category = new Category { Name = "test", SavePath = relativePath };
        Action act = () => this.service.Add(category);
        act.Should().Throw<ArgumentException>().WithMessage("*rooted*");
    }

    [Test]
    public void Add_WhenSavePathIsRootFilesystemDirectory_ThrowsArgumentException()
    {
        var category = new Category { Name = "test", SavePath = "/" };
        Action act = () => this.service.Add(category);
        act.Should().Throw<ArgumentException>().WithMessage("*root filesystem*");
    }

    [TestCase("/etc")]
    [TestCase("/etc/subfolder")]
    [TestCase("/root")]
    [TestCase("/bin")]
    [TestCase("/sbin")]
    [TestCase("/usr")]
    [TestCase("/boot")]
    [TestCase("/sys")]
    [TestCase("/proc")]
    [TestCase("/dev")]
    [TestCase("/downloads/../etc")]
    public void Add_WhenSavePathIsSystemDirectory_ThrowsArgumentException(string forbiddenPath)
    {
        var category = new Category { Name = "test", SavePath = forbiddenPath };
        Action act = () => this.service.Add(category);
        act.Should().Throw<ArgumentException>().WithMessage("*system directory*");
    }

    [Test]
    public void Add_WhenSavePathContainsTraversal_CanonicalizesSavePath()
    {
        var category = new Category { Name = "anime", SavePath = "/downloads/movies/../anime" };
        this.repository.Insert(Arg.Any<Category>()).Returns(ci => ci.Arg<Category>());

        var inserted = this.service.Add(category);

        inserted.SavePath.Should().Be(Path.GetFullPath("/downloads/anime"));
    }

    [Test]
    public void Add_WhenCreateFolderThrowsException_LogsWarningAndAllowsSavePath()
    {
        var diskProvider = Substitute.For<NzbDrone.Common.Disk.IDiskProvider>();
        diskProvider.FolderExists("/downloads/test").Returns(false);
        diskProvider.When(d => d.CreateFolder("/downloads/test")).Do(_ => throw new UnauthorizedAccessException("Permission denied"));
        this.repository.Insert(Arg.Any<Category>()).Returns(ci => ci.Arg<Category>());

        var serviceWithDisk = new CategoryService(this.repository, this.eventAggregator, this.torrentRepository, diskProvider);
        var category = new Category { Name = "test", SavePath = "/downloads/test" };

        var inserted = serviceWithDisk.Add(category);
        inserted.SavePath.Should().Be(Path.GetFullPath("/downloads/test"));
    }

    [Test]
    public void Add_WhenFolderCreatedButNotWritable_ThrowsInvalidOperationException()
    {
        var diskProvider = Substitute.For<NzbDrone.Common.Disk.IDiskProvider>();
        diskProvider.FolderExists("/downloads/readonly").Returns(false, true);
        diskProvider.FolderWritable("/downloads/readonly").Returns(false);

        var serviceWithDisk = new CategoryService(this.repository, this.eventAggregator, this.torrentRepository, diskProvider);
        var category = new Category { Name = "test", SavePath = "/downloads/readonly" };

        Action act = () => serviceWithDisk.Add(category);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not writable*");
    }
}
