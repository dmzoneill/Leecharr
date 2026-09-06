// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
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

    [Test]
    public void Get_ReturnsMaskedApiKey()
    {
        var definition = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://localhost:8989",
            ApiKey = "secret_sonarr_api_key_12345",
        };

        this.repository.Get(1).Returns(definition);

        var result = this.controller.Get(1);
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resource = okResult!.Value as ArrConnectionResource;
        resource.Should().NotBeNull();
        resource!.ApiKey.Should().Be("********");
    }

    [Test]
    public void GetAll_ReturnsMaskedApiKey()
    {
        var list = new List<ArrConnectionDefinition>
        {
            new()
            {
                Id = 1,
                Name = "Sonarr",
                ApiKey = "secret_key",
            },
        };

        this.repository.All().Returns(list);

        var result = this.controller.GetAll();
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        var resources = okResult!.Value as List<ArrConnectionResource>;
        resources.Should().NotBeNull();
        resources![0].ApiKey.Should().Be("********");
    }

    [Test]
    public void Update_PreservesApiKey_WhenMaskedApiKeyProvided()
    {
        var existing = new ArrConnectionDefinition
        {
            Id = 1,
            Name = "Sonarr",
            ArrType = "Sonarr",
            Url = "http://localhost:8989",
            ApiKey = "secret_sonarr_api_key_12345",
        };

        this.repository.Get(1).Returns(existing);

        var resource = new ArrConnectionResource
        {
            Id = 1,
            Name = "Updated Sonarr",
            ArrType = "Sonarr",
            Url = "http://localhost:8989",
            ApiKey = "********",
        };

        var result = this.controller.Update(1, resource);
        var okResult = result.Result as OkObjectResult;

        okResult.Should().NotBeNull();
        this.repository.Received(1).Update(Arg.Is<ArrConnectionDefinition>(c =>
            c.Id == 1 &&
            c.ApiKey == "secret_sonarr_api_key_12345" &&
            c.Name == "Updated Sonarr"));
    }
}
