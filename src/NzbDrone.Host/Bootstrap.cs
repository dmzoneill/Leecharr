using System.Collections.Generic;
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
        "Leecharr.Api.V1"
    };

    public static WebApplication CreateApplication(StartupContext startupContext, string[] urls = null)
    {
        Logger.Info("Starting Leecharr - {0}", BuildInfo.Version);

        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterInstance(startupContext);
        container.AutoAddServices(Assemblies);
        container.Register<IDownloadEngine, DynamicDownloadEngineProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<ITorrentEngineManager, DynamicDownloadEngineProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<INetworkBindingService, DynamicNetworkBindingProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<INetworkBindingManager, DynamicNetworkBindingProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IMediaMetadataService, DynamicMediaMetadataProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IMediaMetadataManager, DynamicMediaMetadataProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IHttpTransportEngine, DynamicHttpTransportProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IHttpTransportManager, DynamicHttpTransportProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IGeoIpService, DynamicGeoIpProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IGeoIpManager, DynamicGeoIpProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IBlocklistService, DynamicBlocklistProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IBlocklistManager, DynamicBlocklistProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IArchiveExtractorService, DynamicArchiveExtractorProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IArchiveExtractorManager, DynamicArchiveExtractorProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IMediaContainerInspector, DynamicMediaInspectorProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IMediaInspectorManager, DynamicMediaInspectorProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IAiService, DynamicAiProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        container.Register<IAiManager, DynamicAiProxy>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            serverOptions.AddServerHeader = false;
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
            var configProvider = app.Services.GetRequiredService<IConfigFileProvider>();
            var url = $"http://{configProvider.BindAddress}:{configProvider.Port}";
            Logger.Info("Listening on {0}", url);
            app.Urls.Add(url);
        }

        return app;
    }

    public static void Start(StartupContext startupContext)
    {
        CreateApplication(startupContext).Run();
    }
}
