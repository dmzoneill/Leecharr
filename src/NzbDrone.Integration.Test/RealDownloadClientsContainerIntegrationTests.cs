// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentAssertions;
using Leecharr.Api.V1.DownloadClients;
using NUnit.Framework;
using NzbDrone.Core.DownloadClients;

namespace NzbDrone.Integration.Test;

[TestFixture]
[Category("LiveClientIntegration")]
public class RealDownloadClientsContainerIntegrationTests
{
    private static bool IsDockerDaemonRunning()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    public async Task TransmissionDaemon_LiveContainer_ConnectsAndQueriesRemoteItems()
    {
        if (!IsDockerDaemonRunning())
        {
            Assert.Ignore("Docker daemon is not reachable in this test environment.");
            return;
        }

        const int transmissionPort = 9091;
        IContainer container = null;

        try
        {
            container = new ContainerBuilder("linuxserver/transmission:latest")
                .WithPortBinding(transmissionPort, true)
                .WithEnvironment("PUID", "1000")
                .WithEnvironment("PGID", "1000")
                .WithEnvironment("USER", "admin")
                .WithEnvironment("PASS", "adminpassword")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(transmissionPort).ForPath("/transmission/rpc").ForStatusCode(System.Net.HttpStatusCode.Conflict)))
                .Build();

            await container.StartAsync();

            var mappedPort = container.GetMappedPublicPort(transmissionPort);
            var host = container.Hostname;

            var clientDef = new DownloadClientDefinition
            {
                Id = 101,
                Name = "LiveTransmissionTest",
                ClientType = "Transmission",
                Host = host,
                Port = mappedPort,
                Username = "admin",
                Password = "adminpassword",
            };

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(clientDef, httpClient);

            items.Should().NotBeNull();
        }
        finally
        {
            if (container != null)
            {
                await container.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task QBittorrentDaemon_LiveContainer_ConnectsAndQueriesRemoteItems()
    {
        if (!IsDockerDaemonRunning())
        {
            Assert.Ignore("Docker daemon is not reachable in this test environment.");
            return;
        }

        const int qbitPort = 8080;
        IContainer container = null;

        try
        {
            container = new ContainerBuilder("linuxserver/qbittorrent:latest")
                .WithPortBinding(qbitPort, true)
                .WithEnvironment("PUID", "1000")
                .WithEnvironment("PGID", "1000")
                .WithEnvironment("WEBUI_PORT", "8080")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(qbitPort).ForPath("/api/v2/app/webapiVersion")))
                .Build();

            await container.StartAsync();

            var mappedPort = container.GetMappedPublicPort(qbitPort);
            var host = container.Hostname;

            var clientDef = new DownloadClientDefinition
            {
                Id = 102,
                Name = "LiveQBittorrentTest",
                ClientType = "qBittorrent",
                Host = host,
                Port = mappedPort,
            };

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(clientDef, httpClient);

            items.Should().NotBeNull();
        }
        finally
        {
            if (container != null)
            {
                await container.DisposeAsync();
            }
        }
    }

    [Test]
    public async Task DelugeDaemon_LiveContainer_ConnectsAndQueriesRemoteItems()
    {
        if (!IsDockerDaemonRunning())
        {
            Assert.Ignore("Docker daemon is not reachable in this test environment.");
            return;
        }

        const int delugeWebPort = 8112;
        IContainer container = null;

        try
        {
            container = new ContainerBuilder("linuxserver/deluge:latest")
                .WithPortBinding(delugeWebPort, true)
                .WithEnvironment("PUID", "1000")
                .WithEnvironment("PGID", "1000")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(delugeWebPort).ForPath("/")))
                .Build();

            await container.StartAsync();

            var mappedPort = container.GetMappedPublicPort(delugeWebPort);
            var host = container.Hostname;

            var clientDef = new DownloadClientDefinition
            {
                Id = 103,
                Name = "LiveDelugeTest",
                ClientType = "Deluge",
                Host = host,
                Port = mappedPort,
                Password = "deluge",
            };

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(clientDef, httpClient);

            items.Should().NotBeNull();
        }
        finally
        {
            if (container != null)
            {
                await container.DisposeAsync();
            }
        }
    }
}
