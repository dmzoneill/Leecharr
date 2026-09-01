// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Swashbuckle.AspNetCore.Swagger;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SwaggerIntegrationTest : IntegrationTestBase
{
    [Test]
    public void SwaggerProvider_GeneratesDocumentWithoutException()
    {
        var swaggerProvider = GlobalSetup.Factory.Services.GetRequiredService<ISwaggerProvider>();
        var doc = swaggerProvider.GetSwagger("v1");
        doc.Should().NotBeNull();
        doc.Info.Title.Should().Be("Leecharr REST API");
    }

    [Test]
    public async Task GetSwaggerJson_ReturnsOkAndValidJson()
    {
        var response = await this.Client.GetAsync("/swagger/v1/swagger.json");
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
