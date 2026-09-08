// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Tags;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Tags;

[TestFixture]
public class TagControllerTest
{
    private ITagRepository tagRepository = null!;
    private ITorrentRepository torrentRepository = null!;
    private INotificationRepository notificationRepository = null!;
    private TagController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.tagRepository = Substitute.For<ITagRepository>();
        this.torrentRepository = Substitute.For<ITorrentRepository>();
        this.notificationRepository = Substitute.For<INotificationRepository>();

        this.controller = new TagController(
            this.tagRepository,
            this.torrentRepository,
            this.notificationRepository);
    }

    [Test]
    public void GetAll_ReturnsAllTagsMappedToResources()
    {
        var tags = new List<Tag>
        {
            new() { Id = 1, Label = "anime" },
            new() { Id = 2, Label = "movies" },
        };

        this.tagRepository.All().Returns(tags);

        var result = this.controller.GetAll();
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resources = okResult!.Value as List<TagResource>;
        resources.Should().NotBeNull();
        resources!.Should().HaveCount(2);
        resources[0].Id.Should().Be(1);
        resources[0].Label.Should().Be("anime");
        resources[1].Id.Should().Be(2);
        resources[1].Label.Should().Be("movies");
    }

    [Test]
    public void Get_WhenExists_ReturnsTagResource()
    {
        var tag = new Tag { Id = 5, Label = "4k" };
        this.tagRepository.Get(5).Returns(tag);

        var result = this.controller.Get(5);
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resource = okResult!.Value as TagResource;
        resource.Should().NotBeNull();
        resource!.Id.Should().Be(5);
        resource.Label.Should().Be("4k");
    }

    [Test]
    public void Get_WhenNotFound_ReturnsNotFound()
    {
        this.tagRepository.Get(99).Returns((Tag)null!);

        var result = this.controller.Get(99);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Create_WhenResourceNullOrEmpty_ReturnsBadRequest()
    {
        var nullResult = this.controller.Create(null!);
        nullResult.Result.Should().BeOfType<BadRequestResult>();

        var emptyResult = this.controller.Create(new TagResource { Label = "   " });
        emptyResult.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Create_WhenTagAlreadyExists_ReturnsExistingTagResource()
    {
        var existing = new Tag { Id = 3, Label = "existing-tag" };
        this.tagRepository.GetByLabel("existing-tag").Returns(existing);

        var result = this.controller.Create(new TagResource { Label = "existing-tag" });
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resource = okResult!.Value as TagResource;
        resource.Should().NotBeNull();
        resource!.Id.Should().Be(3);
        resource.Label.Should().Be("existing-tag");
        this.tagRepository.DidNotReceive().Insert(Arg.Any<Tag>());
    }

    [Test]
    public void Create_WhenValid_InsertsAndReturnsTagResource()
    {
        this.tagRepository.GetByLabel("new-tag").Returns((Tag)null!);
        this.tagRepository.Insert(Arg.Any<Tag>()).Returns(callInfo =>
        {
            var t = callInfo.Arg<Tag>();
            t.Id = 10;
            return t;
        });

        var result = this.controller.Create(new TagResource { Label = "  new-tag  " });
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resource = okResult!.Value as TagResource;
        resource.Should().NotBeNull();
        resource!.Id.Should().Be(10);
        resource.Label.Should().Be("new-tag");
        this.tagRepository.Received(1).Insert(Arg.Is<Tag>(t => t.Label == "new-tag"));
    }

    [Test]
    public void Update_WhenInvalid_ReturnsBadRequest()
    {
        var result1 = this.controller.Update(null!);
        result1.Result.Should().BeOfType<BadRequestResult>();

        var result2 = this.controller.Update(new TagResource { Id = 1, Label = string.Empty });
        result2.Result.Should().BeOfType<BadRequestResult>();

        var result3 = this.controller.Update(new TagResource { Id = 0, Label = "valid" });
        result3.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Update_WhenNotFound_ReturnsNotFound()
    {
        this.tagRepository.Get(10).Returns((Tag)null!);

        var result = this.controller.Update(new TagResource { Id = 10, Label = "updated" });
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Update_WhenValid_UpdatesAndReturnsTagResource()
    {
        var existing = new Tag { Id = 10, Label = "old" };
        this.tagRepository.Get(10).Returns(existing);

        var result = this.controller.Update(new TagResource { Id = 10, Label = "  updated  " });
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resource = okResult!.Value as TagResource;
        resource.Should().NotBeNull();
        resource!.Id.Should().Be(10);
        resource.Label.Should().Be("updated");
        this.tagRepository.Received(1).Update(Arg.Is<Tag>(t => t.Id == 10 && t.Label == "updated"));
    }

    [Test]
    public void Delete_DeletesTagFromRepository()
    {
        this.torrentRepository.All().Returns(new List<Torrent>());
        this.notificationRepository.All().Returns(new List<NotificationDefinition>());

        var result = this.controller.Delete(42);

        result.Should().BeOfType<OkResult>();
        this.tagRepository.Received(1).Delete(42);
    }

    [Test]
    public void Delete_WhenTorrentsContainTagId_CascadesRemovalAndUpdatesTorrents()
    {
        var torrentWithTag = new Torrent
        {
            Id = 1,
            Name = "Torrent 1",
            TagIds = new List<int> { 10, 42, 30 },
        };
        var torrentWithoutTag = new Torrent
        {
            Id = 2,
            Name = "Torrent 2",
            TagIds = new List<int> { 10, 30 },
        };
        var torrentNullTags = new Torrent
        {
            Id = 3,
            Name = "Torrent 3",
            TagIds = null!,
        };

        this.torrentRepository.All().Returns(new List<Torrent> { torrentWithTag, torrentWithoutTag, torrentNullTags });
        this.notificationRepository.All().Returns(new List<NotificationDefinition>());

        var result = this.controller.Delete(42);

        result.Should().BeOfType<OkResult>();
        this.tagRepository.Received(1).Delete(42);

        this.torrentRepository.Received(1).Update(Arg.Is<Torrent>(t => t.Id == 1 && !t.TagIds.Contains(42) && t.TagIds.Count == 2));
        this.torrentRepository.DidNotReceive().Update(Arg.Is<Torrent>(t => t.Id == 2));
        this.torrentRepository.DidNotReceive().Update(Arg.Is<Torrent>(t => t.Id == 3));
    }

    [Test]
    public void Delete_WhenNotificationsContainTagId_CascadesRemovalAndUpdatesNotifications()
    {
        var notificationWithTag = new NotificationDefinition
        {
            Id = 100,
            Name = "Discord Webhook",
            Tags = new List<int> { 42, 99 },
        };
        var notificationWithoutTag = new NotificationDefinition
        {
            Id = 101,
            Name = "Telegram Bot",
            Tags = new List<int> { 1, 2 },
        };
        var notificationNullTags = new NotificationDefinition
        {
            Id = 102,
            Name = "Email",
            Tags = null!,
        };

        this.torrentRepository.All().Returns(new List<Torrent>());
        this.notificationRepository.All().Returns(new List<NotificationDefinition> { notificationWithTag, notificationWithoutTag, notificationNullTags });

        var result = this.controller.Delete(42);

        result.Should().BeOfType<OkResult>();
        this.tagRepository.Received(1).Delete(42);

        this.notificationRepository.Received(1).Update(Arg.Is<NotificationDefinition>(n => n.Id == 100 && !n.Tags.Contains(42) && n.Tags.Count == 1));
        this.notificationRepository.DidNotReceive().Update(Arg.Is<NotificationDefinition>(n => n.Id == 101));
        this.notificationRepository.DidNotReceive().Update(Arg.Is<NotificationDefinition>(n => n.Id == 102));
    }
}
