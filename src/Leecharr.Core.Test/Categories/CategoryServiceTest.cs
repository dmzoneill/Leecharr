// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
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
}
