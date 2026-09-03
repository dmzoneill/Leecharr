// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Ai;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Extraction;
using NzbDrone.Core.Http.Transport;
using NzbDrone.Core.MediaEnrichment.Providers;
using NzbDrone.Core.MediaInspection;
using NzbDrone.Core.Network.Binding;
using NzbDrone.Core.Network.Blocklist;
using NzbDrone.Core.Network.GeoIp;
using NzbDrone.Core.Network.Vpn;
using NzbDrone.Core.Security;

namespace NzbDrone.Host;

public static class Bootstrap
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private static readonly List<string> Assemblies = new()
    {
        "Leecharr.Host",
        "Leecharr.Core",
        "Leecharr.Common",
        "Leecharr.SignalR",
        "Leecharr.Http",
        "Leecharr.Api.V1",
    };

    public static WebApplication CreateApplication(StartupContext startupContext, string[] urls = null)
    {
        Logger.Info("Starting Leecharr - {0}", BuildInfo.Version);

        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance(startupContext);
        container.AutoAddServices(Assemblies);
        container.RegisterSingletonWithInterfaces<DynamicDownloadEngineProxy>();
        container.RegisterSingletonWithInterfaces<DynamicNetworkBindingProxy>();
        container.RegisterSingletonWithInterfaces<VpnKillSwitchService>();
        container.RegisterSingletonWithInterfaces<DynamicMediaMetadataProxy>();
        container.RegisterSingletonWithInterfaces<DynamicHttpTransportProxy>();
        container.RegisterSingletonWithInterfaces<DynamicGeoIpProxy>();
        container.RegisterSingletonWithInterfaces<DynamicBlocklistProxy>();
        container.RegisterSingletonWithInterfaces<DynamicArchiveExtractorProxy>();
        container.RegisterSingletonWithInterfaces<DynamicMediaInspectorProxy>();
        container.RegisterSingletonWithInterfaces<DynamicAiProxy>();

        var builder = WebApplication.CreateBuilder();
        var configProvider = container.Resolve<IConfigFileProvider>();
        var certManager = container.Resolve<ICertificateManager>();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;

            if (urls == null)
            {
                var bindAddress = configProvider.BindAddress?.Trim() ?? "*";
                if (bindAddress is "*" or "0.0.0.0" or "")
                {
                    serverOptions.ListenAnyIP(configProvider.Port);
                    if (configProvider.EnableSsl)
                    {
                        try
                        {
                            var certificate = certManager.GetOrCreateCertificate(configProvider);
                            serverOptions.ListenAnyIP(configProvider.SslPort, listenOptions =>
                            {
                                listenOptions.UseHttps(certificate);
                            });
                            Logger.Info("Configured SSL dual-stack listener on port {0}", configProvider.SslPort);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Failed to initialize SSL listener on port {0}. HTTPS will not be active.", configProvider.SslPort);
                        }
                    }
                }
                else
                {
                    var ip = bindAddress switch
                    {
                        "localhost" or "127.0.0.1" => IPAddress.Loopback,
                        _ when IPAddress.TryParse(bindAddress, out var parsed) => parsed,
                        _ => IPAddress.Any,
                    };

                    serverOptions.Listen(ip, configProvider.Port);

                    if (configProvider.EnableSsl)
                    {
                        try
                        {
                            var certificate = certManager.GetOrCreateCertificate(configProvider);
                            serverOptions.Listen(ip, configProvider.SslPort, listenOptions =>
                            {
                                listenOptions.UseHttps(certificate);
                            });
                            Logger.Info("Configured SSL on {0}:{1}", ip, configProvider.SslPort);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Failed to initialize SSL listener on port {0}. HTTPS will not be active.", configProvider.SslPort);
                        }
                    }
                }
            }
        });

        builder.Host.UseServiceProviderFactory(
            new DryIocServiceProviderFactory(container));

        var startup = new Startup(container);
        startup.ConfigureServices(builder.Services);

        var app = builder.Build();
        startup.Configure(app);

        TableRegistration.RegisterTables();

        var mainDb = app.Services.GetRequiredService<IMainDatabase>();
        Logger.Info("Database initialized: {0}", mainDb.DatabaseType);

        if (urls != null)
        {
            foreach (var url in urls)
            {
                app.Urls.Add(url);
            }
        }
        else
        {
            var httpUrl = $"http://{configProvider.BindAddress}:{configProvider.Port}";
            Logger.Info("Listening on {0}", httpUrl);

            if (configProvider.EnableSsl)
            {
                var httpsUrl = $"https://{configProvider.BindAddress}:{configProvider.SslPort}";
                Logger.Info("Listening with SSL on {0}", httpsUrl);
            }
        }

        return app;
    }

    public static void Start(StartupContext startupContext)
    {
        CreateApplication(startupContext).Run();
    }
}
