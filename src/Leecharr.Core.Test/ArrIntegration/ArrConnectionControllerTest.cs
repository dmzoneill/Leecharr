// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;

namespace Leecharr.Core.Test.ArrIntegration;

[TestFixture]
public class ArrConnectionControllerTest
{
    private IArrConnectionRepository repository = null!;
    private ArrConnectionController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.repository = Substitute.For<IArrConnectionRepository>();
        this.controller = new ArrConnectionController(this.repository);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("RADARR_EXTERNAL_URL", null);
        Environment.SetEnvironmentVariable("SONARR_EXTERNAL_URL", null);
        Environment.SetEnvironmentVariable("LIDARR_EXTERNAL_URL", null);
        Environment.SetEnvironmentVariable("PROWLARR_EXTERNAL_URL", null);
        Environment.SetEnvironmentVariable("LEECHARR__RADARR_EXTERNAL_URL", null);
        Environment.SetEnvironmentVariable("LEECHARR__SONARR_EXTERNAL_URL", null);
        Environment.SetEnvironmentVariable("LEECHARR__ARR_SONARR_PUBLIC_URL", null);
        Environment.SetEnvironmentVariable("LEECHARR__ARR_PUBLIC_URL", null);
    }

    [Test]
    public void GetAll_ReturnsAllConnectionsMappedToResource()
    {
        var models = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = "Sonarr",
                ArrType = "Sonarr",
                Url = "http://sonarr:8989",
                ExternalUrl = "https://sonarr.myhome.net",
                ApiKey = "key1",
                Enable = true,
                SyncCategories = true,
                SyncIntervalMinutes = 15,
            },
            new()
            {
                Id = 2,
                Name = "Radarr",
                ArrType = "Radarr",
                Url = "http://radarr:7878",
                ExternalUrl = null,
                ApiKey = "key2",
                Enable = false,
                SyncCategories = false,
                SyncIntervalMinutes = 30,
            },
        };

        this.repository.All().Returns(models);

        var result = this.controller.GetAll();
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var resources = okResult!.Value as List<ArrConnectionResource>;
        resources.Should().NotBeNull();
        resources!.Should().HaveCount(2);

        resources[0].Id.Should().Be(1);
        resources[0].Name.Should().Be("Sonarr");
        resources[0].ArrType.Should().Be("Sonarr");
        resources[0].Url.Should().Be("http://sonarr:8989");
        resources[0].ExternalUrl.Should().Be("https://sonarr.myhome.net");
        resources[0].PublicUrl.Should().Be("https://sonarr.myhome.net");
        resources[0].Enabled.Should().BeTrue();

        resources[1].Id.Should().Be(2);
        resources[1].ExternalUrl.Should().BeNull();
    }

    [Test]
    public void Get_WhenFound_ReturnsResource()
    {
        var model = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Radarr",
            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ExternalUrl = "https://radarr.local",
            ApiKey = "key",
            Enable = true,
        };
        this.repository.Get(1).Returns(model);

        var result = this.controller.Get(1);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var res = okResult!.Value as ArrConnectionResource;
        res.Should().NotBeNull();
        res!.Id.Should().Be(1);
        res.ExternalUrl.Should().Be("https://radarr.local");
    }

    [Test]
    public void Get_WhenNotFound_ReturnsNotFound()
    {
        this.repository.Get(99).Returns((ArrConnectionDefinition)null!);

        var result = this.controller.Get(99);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Create_WhenResourceNull_ReturnsBadRequest()
    {
        var result = this.controller.Create(null!);
        result.Result.Should().BeOfType<BadRequestResult>();
    }

    [Test]
    public void Create_SavesModelAndReturnsResource()
    {
        var resource = new ArrConnectionResource
        {
            Name = "Lidarr",
            ArrType = "Lidarr",
            Url = "http://lidarr:8686",
            ExternalUrl = "https://lidarr.myhome.net",
            ApiKey = "key3",
            Enabled = true,
        };

        this.repository.Insert(Arg.Any<ArrConnectionDefinition>()).Returns(ci =>
        {
            var def = ci.Arg<ArrConnectionDefinition>();
            def.Id = 10;
            return def;
        });

        var result = this.controller.Create(resource);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var created = okResult!.Value as ArrConnectionResource;
        created.Should().NotBeNull();
        created!.Id.Should().Be(10);
        created.ExternalUrl.Should().Be("https://lidarr.myhome.net");
    }

    [Test]
    public void Update_WhenExisting_UpdatesAndReturnsResource()
    {
        var existing = new ArrConnectionDefinition { Id = 5, Name = "Old" };
        this.repository.Get(5).Returns(existing);

        var resource = new ArrConnectionResource
        {
            Id = 5,
            Name = "Updated Sonarr",
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ExternalUrl = "https://sonarr.updated.net",
            ApiKey = "newkey",
            Enabled = true,
        };

        var result = this.controller.Update(5, resource);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        this.repository.Received(1).Update(Arg.Is<ArrConnectionDefinition>(m =>
            m.Id == 5 &&
            m.Name == "Updated Sonarr" &&
            m.ExternalUrl == "https://sonarr.updated.net"));
    }

    [Test]
    public void Update_WhenNotFound_ReturnsNotFound()
    {
        this.repository.Get(5).Returns((ArrConnectionDefinition)null!);

        var resource = new ArrConnectionResource { Id = 5, Name = "Updated" };
        var result = this.controller.Update(5, resource);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Delete_CallsRepositoryDelete()
    {
        var result = this.controller.Delete(42);
        result.Should().BeOfType<OkResult>();
        this.repository.Received(1).Delete(42);
    }

    [Test]
    public void ResolveExternalUrl_WhenExplicitUrlProvided_ReturnsTrimmedUrl()
    {
        var resolved = ArrConnectionController.ResolveExternalUrl("  https://sonarr.domain.org/  ", "Sonarr", "Sonarr");
        resolved.Should().Be("https://sonarr.domain.org/");
    }

    [Test]
    public void ResolveExternalUrl_WhenEnvVarSet_ReturnsEnvVarUrl()
    {
        Environment.SetEnvironmentVariable("RADARR_EXTERNAL_URL", "https://radarr.public.net");
        var resolved = ArrConnectionController.ResolveExternalUrl(null, "Radarr", "Radarr");
        resolved.Should().Be("https://radarr.public.net");
    }

    [Test]
    public void ResolveExternalUrl_WhenLeecharrPrefixEnvVarSet_ReturnsEnvVarUrl()
    {
        Environment.SetEnvironmentVariable("LEECHARR__SONARR_EXTERNAL_URL", "https://sonarr.leecharr.net");
        var resolved = ArrConnectionController.ResolveExternalUrl(null, "Sonarr", "Sonarr");
        resolved.Should().Be("https://sonarr.leecharr.net");
    }

    [Test]
    public void ResolveExternalUrl_WhenLeecharrArrNamePublicUrlSet_ReturnsEnvVarUrl()
    {
        Environment.SetEnvironmentVariable("LEECHARR__ARR_SONARR_PUBLIC_URL", "https://sonarr.arr.net");
        var resolved = ArrConnectionController.ResolveExternalUrl(null, "Sonarr", "Sonarr");
        resolved.Should().Be("https://sonarr.arr.net");
    }

    [Test]
    public void ResolveExternalUrl_WhenNoUrlOrEnvVar_ReturnsNull()
    {
        var resolved = ArrConnectionController.ResolveExternalUrl(null, "UnknownType", "UnknownName");
        resolved.Should().BeNull();
    }

    [Test]
    public void ResourceSerialization_SupportsBothExternalUrlAndPublicUrl()
    {
        var resource = new ArrConnectionResource
        {
            Name = "Test",
            Url = "http://localhost:8989",
            ExternalUrl = "https://public.example.com",
        };

        var json = JsonSerializer.Serialize(resource);
        json.Should().Contain("\"externalUrl\":\"https://public.example.com\"");
        json.Should().Contain("\"publicUrl\":\"https://public.example.com\"");

        var deserialized = JsonSerializer.Deserialize<ArrConnectionResource>("{\"name\":\"Test\",\"publicUrl\":\"https://alt.example.com\"}");
        deserialized.Should().NotBeNull();
        deserialized!.ExternalUrl.Should().Be("https://alt.example.com");
        deserialized.PublicUrl.Should().Be("https://alt.example.com");
    }

    [Test]
    public async Task Test_WhenNotFound_ReturnsNotFound()
    {
        this.repository.Get(123).Returns((ArrConnectionDefinition)null!);

        var result = await this.controller.Test(123);
        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public async Task TestDirect_WhenUrlEmpty_ReturnsFailResult()
    {
        var resource = new ArrConnectionResource { Url = string.Empty };
        var result = await this.controller.TestDirect(resource);
        var okResult = result.Result as OkObjectResult;
        okResult.Should().NotBeNull();

        var testRes = okResult!.Value as ArrTestResult;
        testRes.Should().NotBeNull();
        testRes!.Success.Should().BeFalse();
        testRes.Message.Should().Be("URL is required.");
    }
}
