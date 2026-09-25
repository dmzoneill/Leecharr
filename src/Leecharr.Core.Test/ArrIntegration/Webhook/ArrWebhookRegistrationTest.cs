// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
                        ""fields"": []
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
