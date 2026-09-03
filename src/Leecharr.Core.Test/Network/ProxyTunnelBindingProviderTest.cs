// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Network.Binding;

namespace Leecharr.Core.Test.Network;

[TestFixture]
public class ProxyTunnelBindingProviderTest
{
    [Test]
    public async Task ConnectTunnelAsync_WhenProxyIsNone_ConnectsDirectly()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var stream = client.GetStream();
            var buf = new byte[4];
            await stream.ReadExactlyAsync(buf, 0, 4);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("PONG"));
        });

        try
        {
            var config = Substitute.For<IConfigService>();
            config.ProxyType.Returns("none");

            var provider = new ProxyTunnelBindingProvider(config);
            using var socket = await provider.ConnectTunnelAsync("127.0.0.1", port);
            socket.Connected.Should().BeTrue();

            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("PING"));
            var reply = new byte[4];
            await stream.ReadExactlyAsync(reply, 0, 4);
            Encoding.ASCII.GetString(reply).Should().Be("PONG");
        }
        finally
        {
            listener.Stop();
            await serverTask;
        }
    }

    [Test]
    public async Task ConnectTunnelAsync_Socks5_PerformsHandshakeAndConnects()
    {
        var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var proxyPort = ((IPEndPoint)proxyListener.LocalEndpoint).Port;

        var proxyServerTask = Task.Run(async () =>
        {
            using var client = await proxyListener.AcceptTcpClientAsync();
            using var stream = client.GetStream();

            // 1. Read Greeting: [0x05, NMETHODS, METHODS...]
            var greeting = new byte[3];
            await stream.ReadExactlyAsync(greeting, 0, 3);
            greeting[0].Should().Be(0x05);

            // Respond with No Auth (0x00)
            await stream.WriteAsync(new byte[] { 0x05, 0x00 });

            // 2. Read Connect Request: [0x05, 0x01, 0x00, ATYP, ADDR..., PORT (2 bytes)]
            var reqHeader = new byte[4];
            await stream.ReadExactlyAsync(reqHeader, 0, 4);
            reqHeader[0].Should().Be(0x05); // SOCKS5
            reqHeader[1].Should().Be(0x01); // CONNECT

            var atyp = reqHeader[3];
            atyp.Should().Be(0x03); // Domain name (DNS leak prevention)
            var domainLen = stream.ReadByte();
            var domainBytes = new byte[domainLen];
            await stream.ReadExactlyAsync(domainBytes, 0, domainLen);
            var domain = Encoding.ASCII.GetString(domainBytes);
            domain.Should().Be("target.tracker.org");

            var portBytes = new byte[2];
            await stream.ReadExactlyAsync(portBytes, 0, 2);
            var targetPort = (portBytes[0] << 8) | portBytes[1];
            targetPort.Should().Be(6969);

            // Respond success: [0x05, 0x00 (SUCCESS), 0x00 (RSV), 0x01 (IPv4), 127, 0, 0, 1, port (2 bytes)]
            await stream.WriteAsync(new byte[] { 0x05, 0x00, 0x00, 0x01, 127, 0, 0, 1, 0x1B, 0x39 });

            // Now proxy payload data
            var dataBuf = new byte[4];
            await stream.ReadExactlyAsync(dataBuf, 0, 4);
            dataBuf.Should().BeEquivalentTo(Encoding.ASCII.GetBytes("TEST"));
            await stream.WriteAsync(Encoding.ASCII.GetBytes("OKAY"));
        });

        try
        {
            var config = Substitute.For<IConfigService>();
            config.ProxyType.Returns("socks5");
            config.ProxyHost.Returns("127.0.0.1");
            config.ProxyPort.Returns(proxyPort);

            var provider = new ProxyTunnelBindingProvider(config);
            using var socket = await provider.ConnectTunnelAsync("target.tracker.org", 6969);
            socket.Connected.Should().BeTrue();

            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("TEST"));
            var reply = new byte[4];
            await stream.ReadExactlyAsync(reply, 0, 4);
            Encoding.ASCII.GetString(reply).Should().Be("OKAY");
        }
        finally
        {
            proxyListener.Stop();
            await proxyServerTask;
        }
    }

    [Test]
    public async Task ConnectTunnelAsync_Http_PerformsConnectAndTunnels()
    {
        var proxyListener = new TcpListener(IPAddress.Loopback, 0);
        proxyListener.Start();
        var proxyPort = ((IPEndPoint)proxyListener.LocalEndpoint).Port;

        var proxyServerTask = Task.Run(async () =>
        {
            using var client = await proxyListener.AcceptTcpClientAsync();
            using var stream = client.GetStream();

            var reader = new StreamReader(stream, Encoding.ASCII);
            var connectLine = await reader.ReadLineAsync();
            connectLine.Should().Be("CONNECT tracker.domain.com:8080 HTTP/1.1");

            // Read remaining headers until empty line
            string line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
            }

            // Send 200 Connection established
            var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n");
            await stream.WriteAsync(response, 0, response.Length);

            // Now proxy payload data
            var dataBuf = new byte[4];
            await stream.ReadExactlyAsync(dataBuf, 0, 4);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("ACK!"));
        });

        try
        {
            var config = Substitute.For<IConfigService>();
            config.ProxyType.Returns("http");
            config.ProxyHost.Returns("127.0.0.1");
            config.ProxyPort.Returns(proxyPort);

            var provider = new ProxyTunnelBindingProvider(config);
            using var socket = await provider.ConnectTunnelAsync("tracker.domain.com", 8080);
            socket.Connected.Should().BeTrue();

            using var stream = new NetworkStream(socket, ownsSocket: false);
            await stream.WriteAsync(Encoding.ASCII.GetBytes("DATA"));
            var reply = new byte[4];
            await stream.ReadExactlyAsync(reply, 0, 4);
            Encoding.ASCII.GetString(reply).Should().Be("ACK!");
        }
        finally
        {
            proxyListener.Stop();
            await proxyServerTask;
        }
    }
}
