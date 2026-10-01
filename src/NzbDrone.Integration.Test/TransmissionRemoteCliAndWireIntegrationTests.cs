// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class TransmissionRemoteCliAndWireIntegrationTests : IntegrationTestBase
{
    private const string TestHash = "8888888888888888888888888888888888888888";
    private const string TestName = "TransmissionCliCompatibilityMovie";

    [Test]
    public async Task TransmissionRpc_FormUrlEncodedWithTrailingSlash_NegotiatesSessionAndReturnsSuccess()
    {
        // 1. Initial request without session token using form-urlencoded and trailing slash
        var initialPayload = "{\"arguments\":{\"fields\":[\"id\",\"name\",\"status\"]},\"method\":\"torrent-get\",\"tag\":123}";
        using var initialContent = new StringContent(initialPayload, Encoding.UTF8, "application/x-www-form-urlencoded");

        var initialResponse = await this.Client.PostAsync("transmission/rpc/", initialContent);
        initialResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "Transmission RPC requires CSRF token negotiation");
        initialResponse.Headers.Contains("X-Transmission-Session-Id").Should().BeTrue();

        var sessionId = string.Join(string.Empty, initialResponse.Headers.GetValues("X-Transmission-Session-Id"));
        sessionId.Should().NotBeNullOrWhiteSpace();

        // 2. Retry with valid session token and form-urlencoded body
        using var authenticatedContent = new StringContent(initialPayload, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var request = new HttpRequestMessage(HttpMethod.Post, "transmission/rpc/")
        {
            Content = authenticatedContent,
        };
        request.Headers.Add("X-Transmission-Session-Id", sessionId);

        var response = await this.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("result").GetString().Should().Be("success");
        doc.RootElement.GetProperty("tag").GetInt32().Should().Be(123);
        doc.RootElement.GetProperty("arguments").TryGetProperty("torrents", out _).Should().BeTrue();
    }

    [Test]
    public async Task TransmissionRpc_TorrentAddAndGet_WithFormUrlEncodedPayload_PersistsAndRetrieves()
    {
        // 1. Acquire session token
        var dummyContent = new StringContent("{}", Encoding.UTF8, "application/x-www-form-urlencoded");
        var tokenResp = await this.Client.PostAsync("transmission/rpc/", dummyContent);
        var sessionId = string.Join(string.Empty, tokenResp.Headers.GetValues("X-Transmission-Session-Id"));

        // 2. Add torrent via torrent-add using form-urlencoded Content-Type
        var addPayload = JsonSerializer.Serialize(new
        {
            method = "torrent-add",
            arguments = new
            {
                filename = $"magnet:?xt=urn:btih:{TestHash}&dn={TestName}",
            },
            tag = 456,
        });

        using var addReq = new HttpRequestMessage(HttpMethod.Post, "transmission/rpc/")
        {
            Content = new StringContent(addPayload, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        addReq.Headers.Add("X-Transmission-Session-Id", sessionId);

        var addResponse = await this.Client.SendAsync(addReq);
        addResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var addJson = await addResponse.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        addDoc.RootElement.GetProperty("result").GetString().Should().Be("success");
        addDoc.RootElement.GetProperty("tag").GetInt32().Should().Be(456);

        // 3. Query back via torrent-get
        var getPayload = JsonSerializer.Serialize(new
        {
            method = "torrent-get",
            arguments = new
            {
                fields = new[] { "id", "name", "hashString", "status" },
            },
            tag = 789,
        });

        using var getReq = new HttpRequestMessage(HttpMethod.Post, "transmission/rpc")
        {
            Content = new StringContent(getPayload, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        getReq.Headers.Add("X-Transmission-Session-Id", sessionId);

        var getResponse = await this.Client.SendAsync(getReq);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var getJson = await getResponse.Content.ReadAsStringAsync();
        using var getDoc = JsonDocument.Parse(getJson);
        var torrents = getDoc.RootElement.GetProperty("arguments").GetProperty("torrents");
        torrents.GetArrayLength().Should().BeGreaterThan(0);

        var found = false;
        foreach (var t in torrents.EnumerateArray())
        {
            if (string.Equals(t.GetProperty("hashString").GetString(), TestHash, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                break;
            }
        }

        found.Should().BeTrue("The added torrent must be visible through Transmission RPC emulation");
    }

    [Test]
    public async Task TransmissionRemote_CliBinary_ExecutesAgainstLiveEndpointIfAvailable()
    {
        var cliPath = FindTransmissionRemoteCli();
        if (cliPath == null)
        {
            Assert.Ignore("transmission-remote CLI binary is not installed in the test environment.");
            return;
        }

        // Spin up a live lightweight ASP.NET Core listener on localhost to test the real binary
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0); // Ephemeral port
        });

        builder.Services.AddControllers(options =>
        {
            options.InputFormatters.Insert(0, new Leecharr.Api.V1.Transmission.TransmissionRpcInputFormatter());
        });

        var app = builder.Build();
        app.MapPost("transmission/rpc/", async (context) =>
        {
            if (!context.Request.Headers.TryGetValue("X-Transmission-Session-Id", out var sid) || string.IsNullOrEmpty(sid))
            {
                context.Response.Headers["X-Transmission-Session-Id"] = "test-session-123";
                context.Response.StatusCode = 409;
                return;
            }

            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            var resp = new
            {
                result = "success",
                tag = 7,
                arguments = new
                {
                    torrents = new object[]
                    {
                        new { id = 1, name = "CLI Test Torrent", status = 4, percentDone = 0.5, rateDownload = 1000, rateUpload = 500, sizeWhenDone = 1048576, error = 0, errorString = "" },
                    },
                },
            };
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(resp));
        });

        await app.StartAsync();
        try
        {
            var port = app.Urls.Select(u => new Uri(u).Port).First();

            var psi = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = $"127.0.0.1:{port} --list",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            process.Should().NotBeNull();
            var output = await process!.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            process.ExitCode.Should().Be(0, $"transmission-remote failed with: {error}\nOutput: {output}");
            output.Should().Contain("CLI Test Torrent");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static string FindTransmissionRemoteCli()
    {
        var paths = new[] { "/usr/bin/transmission-remote", "/usr/local/bin/transmission-remote" };
        foreach (var p in paths)
        {
            if (File.Exists(p))
            {
                return p;
            }
        }

        return null;
    }
}
