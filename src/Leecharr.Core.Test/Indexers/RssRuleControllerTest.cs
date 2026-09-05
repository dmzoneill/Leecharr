// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.Indexers;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Indexers;

namespace Leecharr.Core.Test.Indexers;

[TestFixture]
public class RssRuleControllerTest
{
    private IRssRuleRepository rssRuleRepository = null!;
    private IRssSyncService rssSyncService = null!;
    private RssRuleController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.rssRuleRepository = Substitute.For<IRssRuleRepository>();
        this.rssSyncService = Substitute.For<IRssSyncService>();

        this.controller = new RssRuleController(
            this.rssRuleRepository,
            this.rssSyncService);
    }

    [Test]
    public void GetAll_ReturnsAllRssRules()
    {
        var rules = new List<RssRule>
        {
            new RssRule { Id = 1, Name = "Rule 1", MustContain = "1080p", IsEnabled = true },
            new RssRule { Id = 2, Name = "Rule 2", MustContain = "2160p", IsEnabled = false },
        };

        this.rssRuleRepository.All().Returns(rules);

        var result = this.controller.GetAll();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        var list = okResult!.Value as List<RssRuleResource>;
        list.Should().HaveCount(2);
        list![0].Name.Should().Be("Rule 1");
        list[1].Name.Should().Be("Rule 2");
    }

    [Test]
    public void Get_WhenExists_ReturnsRssRule()
    {
        var rule = new RssRule { Id = 1, Name = "Rule 1", MustContain = "1080p", IsEnabled = true, IndexerIds = new List<int> { 10 } };
        this.rssRuleRepository.Get(1).Returns(rule);

        var result = this.controller.Get(1);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        var resource = okResult!.Value as RssRuleResource;
        resource.Should().NotBeNull();
        resource!.Id.Should().Be(1);
        resource.Name.Should().Be("Rule 1");
        resource.IndexerIds.Should().Contain(10);
    }

    [Test]
    public void Get_WhenNotFound_ReturnsNotFound()
    {
        this.rssRuleRepository.Get(99).Returns((RssRule)null!);

        var result = this.controller.Get(99);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Create_WhenValidResource_InsertsAndReturnsCreated()
    {
        var resource = new RssRuleResource
        {
            Name = "New Rule",
            MustContain = "2160p",
            MustNotContain = "720p",
            MinSeeders = 5,
            FreeleechOnly = true,
            CategoryId = 5040,
            IndexerIds = new List<int> { 1, 2 },
        };

        var inserted = new RssRule
        {
            Id = 42,
            Name = resource.Name,
            MustContain = resource.MustContain,
            MustNotContain = resource.MustNotContain,
            MinSeeders = resource.MinSeeders,
            FreeleechOnly = resource.FreeleechOnly,
            CategoryId = resource.CategoryId,
            IndexerIds = resource.IndexerIds,
        };

        this.rssRuleRepository.Insert(Arg.Any<RssRule>()).Returns(inserted);

        var result = this.controller.Create(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = result.Result as OkObjectResult;
        var createdResource = okResult!.Value as RssRuleResource;
        createdResource.Should().NotBeNull();
        createdResource!.Id.Should().Be(42);
        createdResource.Name.Should().Be("New Rule");
        createdResource.FreeleechOnly.Should().BeTrue();
    }

    [Test]
    public void Create_WhenResourceIsNull_ReturnsBadRequest()
    {
        var result = this.controller.Create(null!);

        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Update_WhenExists_UpdatesAndReturnsOk()
    {
        var existing = new RssRule { Id = 1, Name = "Old Rule" };
        this.rssRuleRepository.Get(1).Returns(existing);

        var resource = new RssRuleResource
        {
            Id = 1,
            Name = "Updated Rule",
            MustContain = "HDR",
            IsEnabled = true,
        };

        var result = this.controller.Update(1, resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.rssRuleRepository.Received(1).Update(Arg.Is<RssRule>(r => r.Id == 1 && r.Name == "Updated Rule"));
    }

    [Test]
    public void Update_WhenNotFound_ReturnsNotFound()
    {
        this.rssRuleRepository.Get(99).Returns((RssRule)null!);

        var resource = new RssRuleResource { Id = 99, Name = "Not Found" };

        var result = this.controller.Update(99, resource);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void UpdateWithoutId_WhenValid_CallsUpdate()
    {
        var existing = new RssRule { Id = 5, Name = "Old Rule" };
        this.rssRuleRepository.Get(5).Returns(existing);

        var resource = new RssRuleResource { Id = 5, Name = "Updated Rule" };

        var result = this.controller.UpdateWithoutId(resource);

        result.Result.Should().BeOfType<OkObjectResult>();
        this.rssRuleRepository.Received(1).Update(Arg.Is<RssRule>(r => r.Id == 5));
    }

    [Test]
    public void Delete_CallsRepositoryDelete()
    {
        var result = this.controller.Delete(7);

        result.Should().BeOfType<OkResult>();
        this.rssRuleRepository.Received(1).Delete(7);
    }

    [Test]
    public async Task SyncRss_WhenCalled_ExecutesSyncService()
    {
        this.rssSyncService.SyncRssFeedsAsync().Returns(Task.FromResult(3));

        var result = await this.controller.SyncRss();

        result.Result.Should().BeOfType<OkObjectResult>();
        await this.rssSyncService.Received(1).SyncRssFeedsAsync();
    }
}
