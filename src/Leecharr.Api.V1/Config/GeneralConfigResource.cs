// Copyright (c) PlaceholderCompany. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class GeneralConfigResource : RestResource
{
    public string InstanceUuid { get; set; }

    public bool AutoStart { get; set; }

    public string ThemeStyle { get; set; }

    public string ColorScheme { get; set; }

    public bool WatchFolderEnabled { get; set; }

    public string WatchFolderPath { get; set; }

    public int WatchFolderScanIntervalSeconds { get; set; }

    public bool WatchFolderAutoStartTorrents { get; set; }

    public bool WatchFolderDeleteAddedTorrents { get; set; }

    public int Port { get; set; }

    public string BindAddress { get; set; }

    public string UrlBase { get; set; }

    public bool AuthenticationEnabled { get; set; }

    public string ApiKey { get; set; }

    public bool EnableSsl { get; set; }

    public int SslPort { get; set; }

    public string SslCertPath { get; set; }

    public string SslKeyPath { get; set; }

    public string SslCertPassword { get; set; }

    public bool RedirectHttpToHttps { get; set; }

    public bool CsrfProtectionEnabled { get; set; } = true;

    public bool HostHeaderValidationEnabled { get; set; } = false;

    public string AllowedHosts { get; set; } = string.Empty;
}

public static class GeneralConfigResourceMapper
{
    public static GeneralConfigResource ToResource(IConfigService config, IConfigFileProvider fileProvider)
    {
        return new GeneralConfigResource
        {
            InstanceUuid = config.InstanceUuid,
            AutoStart = config.AutoStart,
            ThemeStyle = config.ThemeStyle,
            ColorScheme = config.ColorScheme,
            WatchFolderEnabled = config.WatchFolderEnabled,
            WatchFolderPath = config.WatchFolderPath,
            WatchFolderScanIntervalSeconds = config.WatchFolderScanIntervalSeconds,
            WatchFolderAutoStartTorrents = config.WatchFolderAutoStartTorrents,
            WatchFolderDeleteAddedTorrents = config.WatchFolderDeleteAddedTorrents,
            Port = fileProvider.Port,
            BindAddress = fileProvider.BindAddress,
            UrlBase = fileProvider.UrlBase,
            AuthenticationEnabled = fileProvider.AuthenticationEnabled,
            ApiKey = fileProvider.ApiKey.Length > 4
                ? new string('*', fileProvider.ApiKey.Length - 4) + fileProvider.ApiKey[^4..]
                : new string('*', fileProvider.ApiKey.Length),
            EnableSsl = fileProvider.EnableSsl,
            SslPort = fileProvider.SslPort,
            SslCertPath = fileProvider.SslCertPath,
            SslKeyPath = fileProvider.SslKeyPath,
            SslCertPassword = string.IsNullOrEmpty(fileProvider.SslCertPassword) ? string.Empty : "********",
            RedirectHttpToHttps = fileProvider.RedirectHttpToHttps,
            CsrfProtectionEnabled = config.CsrfProtectionEnabled,
            HostHeaderValidationEnabled = config.HostHeaderValidationEnabled,
            AllowedHosts = config.AllowedHosts,
        };
    }
}
