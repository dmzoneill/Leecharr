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

    public bool TerminalAccessEnabled { get; set; } = true;

    public bool AllowPrivateNetworkRequests { get; set; }

    public string AllowedSsrfHostnames { get; set; } = string.Empty;

    public string AllowedSsrfSubnets { get; set; } = string.Empty;
}

public static class GeneralConfigResourceMapper
{
    public static GeneralConfigResource ToResource(IConfigService config, IConfigFileProvider fileProvider)
    {
        var apiKey = fileProvider?.ApiKey;
        var maskedApiKey = !string.IsNullOrEmpty(apiKey)
            ? (apiKey.Length > 4
                ? new string('*', apiKey.Length - 4) + apiKey[^4..]
                : new string('*', apiKey.Length))
            : string.Empty;

        return new GeneralConfigResource
        {
            InstanceUuid = config?.InstanceUuid,
            AutoStart = config?.AutoStart ?? false,
            ThemeStyle = config?.ThemeStyle,
            ColorScheme = config?.ColorScheme,
            WatchFolderEnabled = config?.WatchFolderEnabled ?? false,
            WatchFolderPath = config?.WatchFolderPath,
            WatchFolderScanIntervalSeconds = config?.WatchFolderScanIntervalSeconds ?? 0,
            WatchFolderAutoStartTorrents = config?.WatchFolderAutoStartTorrents ?? false,
            WatchFolderDeleteAddedTorrents = config?.WatchFolderDeleteAddedTorrents ?? false,
            Port = fileProvider?.Port ?? 0,
            BindAddress = fileProvider?.BindAddress,
            UrlBase = fileProvider?.UrlBase,
            AuthenticationEnabled = fileProvider?.AuthenticationEnabled ?? false,
            TerminalAccessEnabled = fileProvider?.TerminalAccessEnabled ?? true,
            ApiKey = maskedApiKey,
            EnableSsl = fileProvider?.EnableSsl ?? false,
            SslPort = fileProvider?.SslPort ?? 0,
            SslCertPath = fileProvider?.SslCertPath,
            SslKeyPath = fileProvider?.SslKeyPath,
            SslCertPassword = string.IsNullOrEmpty(fileProvider?.SslCertPassword) ? string.Empty : "********",
            RedirectHttpToHttps = fileProvider?.RedirectHttpToHttps ?? false,
            CsrfProtectionEnabled = config?.CsrfProtectionEnabled ?? true,
            HostHeaderValidationEnabled = config?.HostHeaderValidationEnabled ?? false,
            AllowedHosts = config?.AllowedHosts ?? string.Empty,
            AllowPrivateNetworkRequests = config?.AllowPrivateNetworkRequests ?? fileProvider?.AllowPrivateNetworkRequests ?? false,
            AllowedSsrfHostnames = config?.AllowedSsrfHostnames ?? fileProvider?.AllowedSsrfHostnames ?? string.Empty,
            AllowedSsrfSubnets = config?.AllowedSsrfSubnets ?? fileProvider?.AllowedSsrfSubnets ?? string.Empty,
        };
    }
}
