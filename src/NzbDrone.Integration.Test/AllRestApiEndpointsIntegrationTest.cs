// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class AllRestApiEndpointsIntegrationTest : IntegrationTestBase
{
    [Test]
    public async Task AllParameterlessGetEndpoints_ReturnSuccessAndNoInternalServerErrors()
    {
        var endpointDataSource = GlobalSetup.Factory.Services.GetRequiredService<EndpointDataSource>();
        var routeEndpoints = endpointDataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        var getEndpoints = new List<string>();
        foreach (var endpoint in routeEndpoints)
        {
            var pattern = endpoint.RoutePattern.RawText;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            // Target native API v1 endpoints without required route parameters like {id}
            if (pattern.StartsWith("api/v1/") && !pattern.Contains('{'))
            {
                var httpMethods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
                if (httpMethods == null || httpMethods.Contains("GET"))
                {
                    var path = "/" + pattern.TrimStart('/');
                    if (!getEndpoints.Contains(path))
                    {
                        getEndpoints.Add(path);
                    }
                }
            }
        }

        getEndpoints.Should().NotBeEmpty("There should be registered parameterless GET endpoints");
        TestContext.Out.WriteLine($"Discovered {getEndpoints.Count} parameterless GET endpoints in Leecharr REST API v1.");

        var failedEndpoints = new List<string>();

        foreach (var path in getEndpoints.OrderBy(p => p))
        {
            try
            {
                using var response = await this.Client.GetAsync(path);
                TestContext.Out.WriteLine($"Testing GET {path} -> {(int)response.StatusCode} {response.StatusCode}");

                if (response.StatusCode == HttpStatusCode.InternalServerError)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    failedEndpoints.Add($"{path} returned 500 InternalServerError: {body}");
                }
                else
                {
                    // Assert it returns a success or redirect status code
                    response.StatusCode.Should().BeOneOf(
                        new[]
                        {
                            HttpStatusCode.OK,
                            HttpStatusCode.NoContent,
                            HttpStatusCode.Accepted,
                            HttpStatusCode.Redirect,
                            HttpStatusCode.MovedPermanently,
                        },
                        $"Endpoint GET {path} should succeed");
                }
            }
            catch (Exception ex)
            {
                failedEndpoints.Add($"{path} threw exception: {ex.Message}");
            }
        }

        failedEndpoints.Should().BeEmpty(
            $"All parameterless GET endpoints should return success without 500 errors. Failures: {string.Join("; ", failedEndpoints)}");
    }

    [Test]
    public async Task PostTestSsl_ReturnsValidCertificateStatus()
    {
        var payload = new
        {
            EnableSsl = true,
            SslPort = 7890,
            SslCertPath = string.Empty,
            BindAddress = "127.0.0.1",
        };

        var response = await this.Client.PostAsJsonAsync("/api/v1/config/general/test-ssl", payload);
        response.IsSuccessStatusCode.Should().BeTrue();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("isValid").GetBoolean().Should().BeTrue();
        root.GetProperty("subject").GetString().Should().Contain("Leecharr");
        root.GetProperty("hasPrivateKey").GetBoolean().Should().BeTrue();
        root.GetProperty("subjectAlternativeNames").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task OpenApiJson_DocumentsAllMajorApiRouteGroups()
    {
        var response = await this.Client.GetAsync("/swagger/v1/swagger.json");
        response.IsSuccessStatusCode.Should().BeTrue();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("paths");

        var expectedRouteSubstrings = new[]
        {
            "/api/v1/config/general",
            "/api/v1/config/seeding",
            "/api/v1/config/advanced",
            "/api/v1/config/network",
            "/api/v1/torrent",
            "/api/v1/categories",
            "/api/v1/system/status",
            "/api/v1/system/task",
            "/api/v1/downloadhistory",
            "/api/v1/subsystems",
            "/api/v1/indexer",
            "/api/v1/health",
        };

        var pathNames = paths.EnumerateObject().Select(p => p.Name).ToList();

        foreach (var expected in expectedRouteSubstrings)
        {
            pathNames.Should().Contain(
                p => p.Contains(expected, StringComparison.OrdinalIgnoreCase),
                $"OpenAPI schema should document endpoint {expected}");
        }
    }

    [Test]
    public async Task ParameterizedEndpoints_WithNonExistentId_ReturnsNotFound()
    {
        var testPaths = new[]
        {
            "/api/v1/torrent/999999",
            "/api/v1/category/999999",
            "/api/v1/tag/999999",
            "/api/v1/arrconnections/999999",
            "/api/v1/backup/999999",
        };

        foreach (var path in testPaths)
        {
            using var response = await this.Client.GetAsync(path);
            TestContext.Out.WriteLine($"Testing nonexistent ID GET {path} -> {(int)response.StatusCode} {response.StatusCode}");
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"Non-existent entity on {path} should return 404 Not Found");
        }
    }

    [Test]
    public async Task CompatibilityLayerEndpoints_ReturnValidResponses()
    {
        // 1. qBittorrent WebAPI v2
        using var qbitVersion = await this.Client.GetAsync("/api/v2/app/webapiVersion");
        qbitVersion.IsSuccessStatusCode.Should().BeTrue();
        var qbitVerText = await qbitVersion.Content.ReadAsStringAsync();
        qbitVerText.Should().NotBeNullOrWhiteSpace();

        using var qbitMainData = await this.Client.GetAsync("/api/v2/sync/maindata");
        qbitMainData.IsSuccessStatusCode.Should().BeTrue();

        // 2. Transmission RPC (negotiate session ID per Transmission RPC spec)
        using var transInit = await this.Client.PostAsJsonAsync("/transmission/rpc", new { method = "session-get" });
        transInit.Headers.Should().ContainKey("X-Transmission-Session-Id");
        var sessionId = transInit.Headers.GetValues("X-Transmission-Session-Id");

        var transReq = new HttpRequestMessage(HttpMethod.Post, "/transmission/rpc")
        {
            Content = JsonContent.Create(new { method = "session-get", tag = 123 }),
        };
        transReq.Headers.Add("X-Transmission-Session-Id", sessionId);
        using var transRpc = await this.Client.SendAsync(transReq);
        transRpc.IsSuccessStatusCode.Should().BeTrue();

        // 3. Deluge JSON-RPC
        using var delugeRpc = await this.Client.PostAsJsonAsync("/json", new
        {
            id = 1,
            method = "core.get_config",
            @params = Array.Empty<object>(),
        });
        delugeRpc.IsSuccessStatusCode.Should().BeTrue();
    }
}
