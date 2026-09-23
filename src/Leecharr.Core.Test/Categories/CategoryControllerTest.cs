// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Categories;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Categories;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Categories;

[TestFixture]
public class CategoryControllerTest
{
    private ICategoryService categoryService = null!;
    private IBroadcastSignalRMessage signalRBroadcaster = null!;
    private CategoryController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.categoryService = Substitute.For<ICategoryService>();
        this.signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();
        this.controller = new CategoryController(this.categoryService, this.signalRBroadcaster);
    }

    [Test]
    public void GetAll_ReturnsMappedCategories()
    {
        var categories = new List<Category>
        {
            new Category
            {
                Id = 1,
                Name = "movies",
                SavePath = "/downloads/movies",
                DefaultUploadLimit = 1000,
                DefaultDownloadLimit = 5000,
                TargetRatio = 2.0,
                TargetSeedTimeMinutes = 1440,
                AutoStop = true,
                IsDefault = true,
            },
        };

        this.categoryService.GetAll().Returns(categories);

        var result = this.controller.GetAll();

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var list = ok.Value as List<CategoryResource>;
        list.Should().NotBeNull();
        list.Should().HaveCount(1);
        list![0].Id.Should().Be(1);
        list[0].Name.Should().Be("movies");
        list[0].SavePath.Should().Be("/downloads/movies");
        list[0].DefaultUploadLimit.Should().Be(1000);
        list[0].DefaultDownloadLimit.Should().Be(5000);
        list[0].TargetRatio.Should().Be(2.0);
        list[0].TargetSeedTimeMinutes.Should().Be(1440);
        list[0].AutoStop.Should().BeTrue();
        list[0].IsDefault.Should().BeTrue();
    }

    [Test]
    public void GetById_WhenCategoryExists_ReturnsOkWithMappedResource()
    {
        var category = new Category
        {
            Id = 5,
            Name = "tv",
            SavePath = "/downloads/tv",
            DefaultUploadLimit = 2000,
            DefaultDownloadLimit = 8000,
            TargetRatio = 1.5,
            TargetSeedTimeMinutes = 2880,
            AutoStop = false,
            IsDefault = false,
        };

        this.categoryService.Get(5).Returns(category);

        var result = this.controller.GetById(5);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var res = ok.Value as CategoryResource;
        res.Should().NotBeNull();
        res!.Id.Should().Be(5);
        res.Name.Should().Be("tv");
        res.SavePath.Should().Be("/downloads/tv");
        res.TargetRatio.Should().Be(1.5);
    }

    [Test]
    public void GetById_WhenCategoryDoesNotExist_ReturnsNotFound()
    {
        this.categoryService.Get(999).Returns((Category)null!);

        var result = this.controller.GetById(999);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Add_WhenResourceIsNull_ReturnsBadRequest()
    {
        var result = this.controller.Add(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result.Result!;
        bad.Value.Should().Be("Category name is required.");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void Add_WhenNameIsWhitespaceOrNull_ReturnsBadRequest(string name)
    {
        var result = this.controller.Add(new CategoryResource { Name = name });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result.Result!;
        bad.Value.Should().Be("Category name is required.");
    }

    [Test]
    public void Add_WhenNormalizedNameIsEmpty_ReturnsBadRequest()
    {
        var result = this.controller.Add(new CategoryResource { Name = " // / " });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result.Result!;
        bad.Value.Should().Be("Category name is required.");
    }

    [Test]
    public void Add_WhenNameContainsNullChar_ReturnsBadRequest()
    {
        var result = this.controller.Add(new CategoryResource { Name = "movies\0invalid" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var bad = (BadRequestObjectResult)result.Result!;
        bad.Value.Should().Be("Category name contains invalid characters.");
    }

    [Test]
    public void Add_WhenRateLimitsNegative_ReturnsBadRequest()
    {
        var res1 = this.controller.Add(new CategoryResource { Name = "valid", DefaultUploadLimit = -1 });
        res1.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)res1.Result!).Value.Should().Be("Default rate limits must be non-negative.");

        var res2 = this.controller.Add(new CategoryResource { Name = "valid", DefaultDownloadLimit = -1 });
        res2.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)res2.Result!).Value.Should().Be("Default rate limits must be non-negative.");
    }

    [Test]
    public void Add_WhenSeedTargetsNegative_ReturnsBadRequest()
    {
        var res1 = this.controller.Add(new CategoryResource { Name = "valid", TargetRatio = -0.5 });
        res1.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)res1.Result!).Value.Should().Be("Seed targets must be non-negative.");

        var res2 = this.controller.Add(new CategoryResource { Name = "valid", TargetSeedTimeMinutes = -10 });
        res2.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)res2.Result!).Value.Should().Be("Seed targets must be non-negative.");
    }

    [Test]
    public void Add_WhenDuplicateNameExists_ReturnsBadRequest()
    {
        var existing = new Category { Id = 1, Name = "music" };
        this.categoryService.GetByName("music").Returns(existing);

        var resource = new CategoryResource { Name = "music" };
        var result = this.controller.Add(resource);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("A category with name 'music' already exists.");
    }

    [Test]
    public void Add_WhenValid_TrimsSavePathAndNormalizesNameAndCallsService()
    {
        var resource = new CategoryResource
        {
            Name = " music/rock ",
            SavePath = " /downloads/music/rock ",
            DefaultUploadLimit = 500,
            DefaultDownloadLimit = 2000,
            TargetRatio = 3.0,
            TargetSeedTimeMinutes = 720,
            AutoStop = true,
            IsDefault = false,
        };

        this.categoryService.GetByName("music/rock").Returns((Category)null!);
        this.categoryService.Add(Arg.Any<Category>()).Returns(args =>
        {
            var c = (Category)args[0];
            c.Id = 42;
            return c;
        });

        var result = this.controller.Add(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        var ok = (OkObjectResult)result.Result!;
        var res = ok.Value as CategoryResource;
        res.Should().NotBeNull();
        res!.Id.Should().Be(42);
        res.Name.Should().Be("music/rock");
        res.SavePath.Should().Be("/downloads/music/rock");
        res.TargetRatio.Should().Be(3.0);
        res.AutoStop.Should().BeTrue();

        this.categoryService.Received(1).Add(Arg.Is<Category>(c =>
            c.Name == "music/rock" &&
            c.SavePath == "/downloads/music/rock" &&
            c.AutoStop == true &&
            c.TargetRatio == 3.0));
    }

    [Test]
    public void Add_WhenServiceThrowsArgumentException_ReturnsBadRequest()
    {
        this.categoryService.GetByName("valid").Returns((Category)null!);
        this.categoryService.Add(Arg.Any<Category>()).Throws(new ArgumentException("Save path is invalid."));

        var result = this.controller.Add(new CategoryResource { Name = "valid", SavePath = "/invalid" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("Save path is invalid.");
    }

    [Test]
    public void Add_WhenServiceThrowsInvalidOperationException_ReturnsBadRequest()
    {
        this.categoryService.GetByName("valid").Returns((Category)null!);
        this.categoryService.Add(Arg.Any<Category>()).Throws(new InvalidOperationException("Forbidden directory."));

        var result = this.controller.Add(new CategoryResource { Name = "valid" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("Forbidden directory.");
    }

    [Test]
    public void Update_WhenResourceNullOrNameWhitespace_ReturnsBadRequest()
    {
        this.controller.Update(1, null!).Result.Should().BeOfType<BadRequestObjectResult>();
        this.controller.Update(1, new CategoryResource { Name = " " }).Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Update_WhenCategoryNotFound_ReturnsNotFound()
    {
        this.categoryService.Get(99).Returns((Category)null!);

        var result = this.controller.Update(99, new CategoryResource { Name = "tv" });

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Update_WhenNormalizedNameIsEmpty_ReturnsBadRequest()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "old" });

        var result = this.controller.Update(1, new CategoryResource { Name = "///" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("Category name is required.");
    }

    [Test]
    public void Update_WhenNameContainsNullChar_ReturnsBadRequest()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "old" });

        var result = this.controller.Update(1, new CategoryResource { Name = "bad\0name" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("Category name contains invalid characters.");
    }

    [Test]
    public void Update_WhenRateLimitsOrSeedTargetsNegative_ReturnsBadRequest()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "old" });

        this.controller.Update(1, new CategoryResource { Name = "valid", DefaultUploadLimit = -1 })
            .Result.Should().BeOfType<BadRequestObjectResult>();
        this.controller.Update(1, new CategoryResource { Name = "valid", DefaultDownloadLimit = -1 })
            .Result.Should().BeOfType<BadRequestObjectResult>();
        this.controller.Update(1, new CategoryResource { Name = "valid", TargetRatio = -1 })
            .Result.Should().BeOfType<BadRequestObjectResult>();
        this.controller.Update(1, new CategoryResource { Name = "valid", TargetSeedTimeMinutes = -1 })
            .Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Update_WhenDuplicateNameExistsWithDifferentId_ReturnsBadRequest()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "anime" });
        this.categoryService.GetByName("manga").Returns(new Category { Id = 2, Name = "manga" });

        var result = this.controller.Update(1, new CategoryResource { Name = "manga" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("A category with name 'manga' already exists.");
    }

    [Test]
    public void Update_WhenSameCategoryUpdatesItself_Succeeds()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "anime", SavePath = "/old" });
        this.categoryService.GetByName("anime").Returns(new Category { Id = 1, Name = "anime" });
        this.categoryService.Update(Arg.Any<Category>()).Returns(args => (Category)args[0]);

        var result = this.controller.Update(1, new CategoryResource
        {
            Name = "anime",
            SavePath = " /new ",
            AutoStop = true,
            TargetRatio = 2.5,
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var res = ((OkObjectResult)result.Result!).Value as CategoryResource;
        res!.Id.Should().Be(1);
        res.SavePath.Should().Be("/new");
        res.AutoStop.Should().BeTrue();
        res.TargetRatio.Should().Be(2.5);
    }

    [Test]
    public void Update_WhenValid_SetsIdAndCallsService()
    {
        this.categoryService.Get(7).Returns(new Category { Id = 7, Name = "oldname" });
        this.categoryService.GetByName("newname").Returns((Category)null!);
        this.categoryService.Update(Arg.Any<Category>()).Returns(args => (Category)args[0]);

        var result = this.controller.Update(7, new CategoryResource
        {
            Name = " newname ",
            SavePath = " /downloads/newname ",
            DefaultDownloadLimit = 1500,
            DefaultUploadLimit = 500,
        });

        result.Result.Should().BeOfType<OkObjectResult>();
        var res = ((OkObjectResult)result.Result!).Value as CategoryResource;
        res!.Id.Should().Be(7);
        res.Name.Should().Be("newname");
        res.SavePath.Should().Be("/downloads/newname");

        this.categoryService.Received(1).Update(Arg.Is<Category>(c =>
            c.Id == 7 &&
            c.Name == "newname" &&
            c.SavePath == "/downloads/newname"));
    }

    [Test]
    public void Update_WhenServiceThrowsArgumentException_ReturnsBadRequest()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "anime" });
        this.categoryService.GetByName("anime").Returns((Category)null!);
        this.categoryService.Update(Arg.Any<Category>()).Throws(new ArgumentException("Invalid path."));

        var result = this.controller.Update(1, new CategoryResource { Name = "anime" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("Invalid path.");
    }

    [Test]
    public void Update_WhenServiceThrowsInvalidOperationException_ReturnsBadRequest()
    {
        this.categoryService.Get(1).Returns(new Category { Id = 1, Name = "anime" });
        this.categoryService.GetByName("anime").Returns((Category)null!);
        this.categoryService.Update(Arg.Any<Category>()).Throws(new InvalidOperationException("Invalid operation."));

        var result = this.controller.Update(1, new CategoryResource { Name = "anime" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        ((BadRequestObjectResult)result.Result!).Value.Should().Be("Invalid operation.");
    }

    [Test]
    public void Delete_CallsCategoryServiceDelete_ReturnsNoContent()
    {
        var result = this.controller.Delete(42);

        result.Should().BeOfType<NoContentResult>();
        this.categoryService.Received(1).Delete(42);
    }

    [Test]
    public void CategoryResourceMapper_ToResource_And_ToModel_MapsAllProperties()
    {
        CategoryResourceMapper.ToResource(null!).Should().BeNull();
        CategoryResourceMapper.ToModel(null!).Should().BeNull();

        var model = new Category
        {
            Id = 10,
            Name = "games",
            SavePath = "/downloads/games",
            DefaultUploadLimit = 1000,
            DefaultDownloadLimit = 5000,
            TargetRatio = 1.75,
            TargetSeedTimeMinutes = 120,
            AutoStop = true,
            IsDefault = true,
        };

        var resource = CategoryResourceMapper.ToResource(model);

        resource.Should().NotBeNull();
        resource.Id.Should().Be(10);
        resource.Name.Should().Be("games");
        resource.SavePath.Should().Be("/downloads/games");
        resource.DefaultUploadLimit.Should().Be(1000);
        resource.DefaultDownloadLimit.Should().Be(5000);
        resource.TargetRatio.Should().Be(1.75);
        resource.TargetSeedTimeMinutes.Should().Be(120);
        resource.AutoStop.Should().BeTrue();
        resource.IsDefault.Should().BeTrue();

        var roundtrippedModel = CategoryResourceMapper.ToModel(resource);

        roundtrippedModel.Should().NotBeNull();
        roundtrippedModel.Id.Should().Be(10);
        roundtrippedModel.Name.Should().Be("games");
        roundtrippedModel.SavePath.Should().Be("/downloads/games");
        roundtrippedModel.DefaultUploadLimit.Should().Be(1000);
        roundtrippedModel.DefaultDownloadLimit.Should().Be(5000);
        roundtrippedModel.TargetRatio.Should().Be(1.75);
        roundtrippedModel.TargetSeedTimeMinutes.Should().Be(120);
        roundtrippedModel.AutoStop.Should().BeTrue();
        roundtrippedModel.IsDefault.Should().BeTrue();
    }
}
