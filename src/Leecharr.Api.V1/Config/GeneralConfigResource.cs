// Copyright (c) FeedItOut. All rights reserved.

using Leecharr.Http.REST;
using NzbDrone.Core.Authentication;
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

    public int WatchFolderScanIntervalSeconds { get; set; } = 10;

    public bool WatchFolderAutoStartTorrents { get; set; }

    public bool WatchFolderDeleteAddedTorrents { get; set; }

    public int Port { get; set; } = 7889;

    public string Branch { get; set; } = "main";

    public string BindAddress { get; set; }

    public string UrlBase { get; set; }

    public bool AuthenticationEnabled { get; set; }

    public AuthenticationRequiredType AuthenticationRequired { get; set; }

    public string ApiKey { get; set; }

    public bool EnableSsl { get; set; }

    public int SslPort { get; set; } = 7890;

    public string SslCertPath { get; set; }

    public string SslKeyPath { get; set; }

    public string SslCertPassword { get; set; }

    public bool RedirectHttpToHttps { get; set; }

    public bool CsrfProtectionEnabled { get; set; } = true;

    public bool HostHeaderValidationEnabled { get; set; } = false;

    public string AllowedHosts { get; set; } = string.Empty;

    public string AllowedCorsOrigins { get; set; } = string.Empty;

    public bool TerminalAccessEnabled { get; set; } = true;

    public bool AllowPrivateNetworkRequests { get; set; } = true;

    public string AllowedSsrfHostnames { get; set; } = string.Empty;

    public string AllowedSsrfSubnets { get; set; } = string.Empty;
}

public static class GeneralConfigResourceMapper
{
    public const string MaskedSslCertSecretPlaceholder = "********"; /* not a credential: mask placeholder/header name/empty or vendor default */ // nosemgrep

    public static bool IsMaskedApiKey(string submitted, string storedApiKey)
    {
        if (string.IsNullOrEmpty(submitted) || string.IsNullOrEmpty(storedApiKey))
        {
            return false;
        }

        if (submitted == storedApiKey)
        {
            return true;
        }

        var masked = storedApiKey.Length > 4
            ? new string('*', storedApiKey.Length - 4) + storedApiKey[^4..]
            : new string('*', storedApiKey.Length);

        return submitted == masked;
    }

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
            Branch = !string.IsNullOrWhiteSpace(fileProvider?.Branch)
                ? fileProvider.Branch
                : (!string.IsNullOrWhiteSpace(config?.Branch) ? config.Branch : "main"),
            BindAddress = fileProvider?.BindAddress,
            UrlBase = fileProvider?.UrlBase,
            AuthenticationEnabled = fileProvider?.AuthenticationEnabled ?? false,
            AuthenticationRequired = fileProvider?.AuthenticationRequired ?? AuthenticationRequiredType.DisabledForLocalAddresses,
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
            AllowedCorsOrigins = GetConfigStringOrFileFallback(config?.AllowedCorsOrigins, fileProvider?.AllowedCorsOrigins),
            AllowPrivateNetworkRequests = GetAllowPrivateNetworkRequests(config, fileProvider),
            AllowedSsrfHostnames = GetConfigStringOrFileFallback(config?.AllowedSsrfHostnames, fileProvider?.AllowedSsrfHostnames),
            AllowedSsrfSubnets = GetConfigStringOrFileFallback(config?.AllowedSsrfSubnets, fileProvider?.AllowedSsrfSubnets),
        };
    }

    private static string GetConfigStringOrFileFallback(string configValue, string fileValue)
    {
        return !string.IsNullOrWhiteSpace(configValue) ? configValue : fileValue ?? string.Empty;
    }

    private static bool GetAllowPrivateNetworkRequests(IConfigService config, IConfigFileProvider fileProvider)
    {
        if (config == null)
        {
            return fileProvider?.AllowPrivateNetworkRequests ?? true;
        }

        var raw = config.GetValue("AllowPrivateNetworkRequests", string.Empty);
        if (string.IsNullOrEmpty(raw))
        {
            return fileProvider?.AllowPrivateNetworkRequests ?? true;
        }

        return config.GetValueBoolean("AllowPrivateNetworkRequests", true);
    }
}
