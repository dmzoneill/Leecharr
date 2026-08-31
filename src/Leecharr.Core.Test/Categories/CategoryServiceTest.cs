// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test.Categories;

[TestFixture]
public class CategoryServiceTest
{
    private ICategoryRepository repository = null!;
    private IEventAggregator eventAggregator = null!;
    private CategoryService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<ICategoryRepository>();
        this.eventAggregator = Substitute.For<IEventAggregator>();
        this.service = new CategoryService(this.repository, this.eventAggregator);
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
    public void Delete_CallsRepositoryDelete()
    {
        this.service.Delete(5);
        this.repository.Received(1).Delete(5);
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
