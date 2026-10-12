// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.ArrIntegration;
using NzbDrone.Core.ArrIntegration.Webhook;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;

namespace Leecharr.Core.Test.ArrIntegration.Webhook;

[TestFixture]
public class ArrWebhookRegistrationTest
{
    private IConfigFileProvider configFileProvider = null!;

    [SetUp]
    public void SetUp()
    {
        this.configFileProvider = Substitute.For<IConfigFileProvider>();
        this.configFileProvider.ApiKey.Returns("leecharr-key-12345");
        this.configFileProvider.Port.Returns(7889);
        this.configFileProvider.EnableSsl.Returns(false);
        this.configFileProvider.BindAddress.Returns("0.0.0.0");
        this.configFileProvider.UrlBase.Returns(string.Empty);
    }

    [Test]
    public void RegisterWebhook_WhenWebhookDisabled_ReturnsTrueWithoutCallingHttp()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "key",
            Enable = true,
            WebhookEnabled = false,
        };

        var result = registration.RegisterWebhook(conn);
        result.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Test]
    public void RegisterWebhook_WhenArrTypeProwlarr_SkipsAndReturnsTrue()
    {
        var handler = new MockHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Prowlarr",
            Url = "http://prowlarr:9696",
            ApiKey = "key",
            Enable = true,
            WebhookEnabled = true,
        };

        var result = registration.RegisterWebhook(conn);
        result.Should().BeTrue();
        handler.CallCount.Should().Be(0);
    }

    [Test]
    public void RegisterWebhook_WhenUrlIncludesApiV3Suffix_UsesNormalizedNotificationPath()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Created);
        });
        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989/api/v3",
            ApiKey = "key",
            Enable = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local",
        };

        registration.RegisterWebhook(conn).Should().BeTrue();
        requests.Should().Contain(r => r.RequestUri!.ToString() == "http://sonarr:8989/api/v3/notification");
    }

    [Test]
    public void RegisterWebhook_WhenNoExistingWebhook_CreatesViaPost()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 42}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-api-key",
            Enable = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local",
        };

        var result = registration.RegisterWebhook(conn);
        result.Should().BeTrue();

        requests.Should().HaveCount(2);
        requests[0].Method.Should().Be(HttpMethod.Get);
        requests[0].RequestUri!.ToString().Should().Be("http://sonarr:8989/api/v3/notification");

        requests[1].Method.Should().Be(HttpMethod.Post);
        requests[1].RequestUri!.ToString().Should().Be("http://sonarr:8989/api/v3/notification");
        requests[1].Headers.GetValues("X-Api-Key").Should().Contain("sonarr-api-key");
    }

    [Test]
    public void RegisterWebhook_WhenExistingWebhookHasSameUrlAndKey_DoesNotPost()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                var existingJson = @"[
                    {
                        ""id"": 10,
                        ""name"": ""Leecharr"",
                        ""fields"": [
                            { ""name"": ""url"", ""value"": ""http://leecharr.local:7889/api/v1/webhook/arr"" },
                            { ""name"": ""headers"", ""value"": [ { ""key"": ""X-Api-Key"", ""value"": ""leecharr-key-12345"" } ] }
                        ]
                    }
                ]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existingJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-api-key",
            Enable = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local",
        };

        var result = registration.RegisterWebhook(conn);
        result.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Test]
    public void RegisterWebhook_WhenSeedarrWebhookUsesArrPath_CreatesLeecharrViaPostWithoutUpdatingSeedarr()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                var existingJson = @"[
                    {
                        ""id"": 1,
                        ""name"": ""Seedarr"",
                        ""implementation"": ""Webhook"",
                        ""fields"": [
                            { ""name"": ""url"", ""value"": ""http://seedarr.local:9898/api/v1/webhook/arr"" }
                        ]
                    }
                ]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existingJson),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 42}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-api-key",
            Enable = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local",
        };

        registration.RegisterWebhook(conn).Should().BeTrue();
        requests.Should().Contain(r => r.Method == HttpMethod.Post);
        requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Test]
    public void UnregisterWebhook_WhenExistingFound_SendsDelete()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                var existingJson = @"[
                    {
                        ""id"": 15,
                        ""name"": ""Leecharr"",
                        ""implementation"": ""Webhook"",
                        ""fields"": [
                            { ""name"": ""url"", ""value"": ""http://leecharr.local:7889/api/v1/webhook/arr"" }
                        ]
                    }
                ]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existingJson),
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Radarr",
            Url = "http://radarr:7878",
            ApiKey = "radarr-key",
        };

        var result = registration.UnregisterWebhook(conn);
        result.Should().BeTrue();

        requests.Should().HaveCount(2);
        requests[1].Method.Should().Be(HttpMethod.Delete);
        requests[1].RequestUri!.ToString().Should().Be("http://radarr:7878/api/v3/notification/15");
    }

    [Test]
    public void RegisterWebhook_WhenNotificationNamedLeecharrButNotWebhookUrl_CreatesViaPostWithoutDeletingOther()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                var existingJson = @"[
                    {
                        ""id"": 99,
                        ""name"": ""Leecharr"",
                        ""implementation"": ""Discord"",
                        ""fields"": [
                            { ""name"": ""webHookUrl"", ""value"": ""https://discord.com/api/webhooks/abc"" }
                        ]
                    }
                ]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existingJson),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 100}"),
                };
            }

            if (req.Method == HttpMethod.Put || req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-api-key",
            Enable = true,
            WebhookEnabled = true,
            WebhookHost = "leecharr.local",
        };

        var result = registration.RegisterWebhook(conn);
        result.Should().BeTrue();

        requests.Should().HaveCount(2);
        requests[0].Method.Should().Be(HttpMethod.Get);
        requests[1].Method.Should().Be(HttpMethod.Post);
        requests.Should().NotContain(r => r.Method == HttpMethod.Put);
    }

    [Test]
    public void UnregisterWebhook_WhenNotificationNamedLeecharrOnly_DoesNotDelete()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                var existingJson = @"[
                    {
                        ""id"": 20,
                        ""name"": ""Leecharr"",
                        ""implementation"": ""Slack"",
                        ""fields"": []
                    }
                ]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existingJson),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-key",
        };

        var result = registration.UnregisterWebhook(conn);
        result.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    [Test]
    public void RegisterDownloadClient_WhenAutomaticAddEnabled_RegistersDelugeClient()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 99}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-key",
            Enable = true,
            EnableAutomaticAdd = true,
            WebhookHost = "leecharr.local",
            Category = "custom-tv",
        };

        var result = registration.RegisterDownloadClient(conn);
        result.Should().BeTrue();

        requests.Should().HaveCount(2);
        requests[0].Method.Should().Be(HttpMethod.Get);
        requests[0].RequestUri!.ToString().Should().Be("http://sonarr:8989/api/v3/downloadclient");

        requests[1].Method.Should().Be(HttpMethod.Post);
        requests[1].RequestUri!.ToString().Should().Be("http://sonarr:8989/api/v3/downloadclient");
    }

    [Test]
    public void RegisterDownloadClient_WhenWebhookHostIsAbsoluteUrl_UsesHostAndPortFromUrl()
    {
        string postBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                postBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 100}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-key",
            Enable = true,
            EnableAutomaticAdd = true,
            WebhookHost = "https://leecharr.example:8443",
        };

        registration.RegisterDownloadClient(conn).Should().BeTrue();
        postBody.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(postBody!);
        var fields = doc.RootElement.GetProperty("fields");
        GetFieldValue(fields, "host").Should().Be("leecharr.example");
        GetFieldValue(fields, "port").Should().Be("8443");
    }

    [Test]
    public void RegisterWebhook_WhenWebhookHostIsRawIpv6_RegistersBracketedWebhookUrl()
    {
        string postBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                postBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 42}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-api-key",
            Enable = true,
            WebhookEnabled = true,
            WebhookHost = "2001:db8::10",
        };

        registration.RegisterWebhook(conn).Should().BeTrue();
        postBody.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(postBody!);
        var fields = doc.RootElement.GetProperty("fields");
        GetFieldValue(fields, "url").Should().Be("http://[2001:db8::10]:7889/api/v1/webhook/arr");
    }

    [Test]
    public void RegisterDownloadClient_WhenWhisparr_IncludesMovieCategoryInPostBody()
    {
        var requests = new List<HttpRequestMessage>();
        string postBody = null;
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                postBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 88}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Whisparr",
            Url = "http://whisparr:6969",
            ApiKey = "whisparr-key",
            Enable = true,
            EnableAutomaticAdd = true,
            WebhookHost = "leecharr.local",
        };

        registration.RegisterDownloadClient(conn).Should().BeTrue();

        requests.Should().HaveCount(2);
        requests[1].RequestUri!.ToString().Should().Be("http://whisparr:6969/api/v3/downloadclient");

        postBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(postBody);
        GetFieldValue(doc.RootElement.GetProperty("fields"), "movieCategory").Should().Be("whisparr");
    }

    [Test]
    public void UnregisterDownloadClient_WhenExistingFound_SendsDelete()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new MockHttpMessageHandler(req =>
        {
            requests.Add(req);
            if (req.Method == HttpMethod.Get)
            {
                var existingJson = @"[
                    {
                        ""id"": 77,
                        ""name"": ""Leecharr""
                    }
                ]";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existingJson),
                };
            }

            if (req.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://sonarr:8989",
            ApiKey = "sonarr-key",
        };

        var result = registration.UnregisterDownloadClient(conn);
        result.Should().BeTrue();

        requests.Should().HaveCount(2);
        requests[1].Method.Should().Be(HttpMethod.Delete);
        requests[1].RequestUri!.ToString().Should().Be("http://sonarr:8989/api/v3/downloadclient/77");
    }

    [Test]
    public void RegisterDownloadClient_WhenArrUrlIsLocalhost_UsesLoopbackForDelugeHost()
    {
        var postBody = string.Empty;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                postBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 1}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://localhost:8989",
            ApiKey = "sonarr-key",
            Enable = true,
            EnableAutomaticAdd = true,
        };

        registration.RegisterDownloadClient(conn).Should().BeTrue();
        GetDownloadClientHostFromBody(postBody).Should().Be("127.0.0.1");
    }

    [Test]
    public void RegisterDownloadClient_WhenArrHostOnlyStartsWithLocalhost_DoesNotUseLoopbackForDelugeHost()
    {
        var postBody = string.Empty;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                postBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 1}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        foreach (var arrUrl in new[] { "http://localhost.home.arpa:8989", "http://localhostess.example:8989" })
        {
            postBody = string.Empty;
            var conn = new ArrConnectionDefinition
            {
                ArrType = "Sonarr",
                Url = arrUrl,
                ApiKey = "sonarr-key",
                Enable = true,
                EnableAutomaticAdd = true,
            };

            registration.RegisterDownloadClient(conn).Should().BeTrue();
            GetDownloadClientHostFromBody(postBody).Should().NotBe("127.0.0.1");
        }
    }

    [Test]
    public void RegisterWebhook_WhenArrHostOnlyStartsWithLocalhost_WebhookUrlDoesNotUseLoopback()
    {
        var postBody = string.Empty;
        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                };
            }

            if (req.Method == HttpMethod.Post)
            {
                postBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("{\"id\": 42}"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var client = new HttpClient(handler);
        var registration = new ArrWebhookRegistration(this.configFileProvider, client);

        var conn = new ArrConnectionDefinition
        {
            ArrType = "Sonarr",
            Url = "http://localhost.home.arpa:8989",
            ApiKey = "sonarr-api-key",
            Enable = true,
            WebhookEnabled = true,
        };

        registration.RegisterWebhook(conn).Should().BeTrue();
        postBody.Should().NotContain("127.0.0.1");
    }

    private static string GetDownloadClientHostFromBody(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var field in doc.RootElement.GetProperty("fields").EnumerateArray())
        {
            if (field.GetProperty("name").GetString() == "host")
            {
                return field.GetProperty("value").GetString()!;
            }
        }

        throw new InvalidOperationException("Deluge host field not found in download client payload.");
    }

    [Test]
    public void ArrWebhookMaintenanceTask_RegisterAllConnections_CallsWebhookRegistrationForEnabled()
    {
        var repo = Substitute.For<IArrConnectionRepository>();
        var mockReg = Substitute.For<IArrWebhookRegistration>();
        mockReg.Register(Arg.Any<ArrConnectionDefinition>()).Returns(true);

        var conns = new List<ArrConnectionDefinition>
        {
            new() { Id = 1, ArrType = "Sonarr", Enable = true, WebhookEnabled = true },
            new() { Id = 2, ArrType = "Radarr", Enable = true, EnableAutomaticAdd = true },
            new() { Id = 3, ArrType = "Lidarr", Enable = false, WebhookEnabled = true },
        };

        repo.All().Returns(conns);

        var task = new ArrWebhookMaintenanceTask(repo, mockReg);
        var failed = task.RegisterAllConnections();

        failed.Should().BeEmpty();
        mockReg.Received(1).Register(Arg.Is<ArrConnectionDefinition>(c => c.Id == 1));
        mockReg.Received(1).Register(Arg.Is<ArrConnectionDefinition>(c => c.Id == 2));
        mockReg.DidNotReceive().Register(Arg.Is<ArrConnectionDefinition>(c => c.Id == 3));
    }

    [Test]
    public async Task ArrWebhookMaintenanceTask_Handle_RunsRegistrationTask()
    {
        var repo = Substitute.For<IArrConnectionRepository>();
        var mockReg = Substitute.For<IArrWebhookRegistration>();
        mockReg.Register(Arg.Any<ArrConnectionDefinition>()).Returns(true);

        repo.All().Returns(new List<ArrConnectionDefinition>
        {
            new() { Id = 1, ArrType = "Sonarr", Enable = true, WebhookEnabled = true },
        });

        var task = new ArrWebhookMaintenanceTask(repo, mockReg);
        task.Handle(new ApplicationStartedEvent());

        if (task.LastStartupTask != null)
        {
            await task.LastStartupTask;
        }

        mockReg.Received(1).Register(Arg.Is<ArrConnectionDefinition>(c => c.Id == 1));
    }

    private static string GetFieldValue(JsonElement fields, string name)
    {
        foreach (var field in fields.EnumerateArray())
        {
            if (field.GetProperty("name").GetString() == name)
            {
                return field.GetProperty("value").ToString();
            }
        }

        return string.Empty;
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

        public int CallCount { get; private set; }

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            this.handler = handler;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.CallCount++;
            return this.handler(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.CallCount++;
            return Task.FromResult(this.handler(request));
        }
    }
}
