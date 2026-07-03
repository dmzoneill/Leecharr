using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Core.Configuration;

public class ConfigFileProvider : IConfigFileProvider
{
    private const string ConfigFileName = "config.xml";
    private const string ConfigElementName = "Config";

    private readonly string _configFile;
    private readonly Dictionary<string, string> _config;
    private static readonly object Mutex = new();

    public ConfigFileProvider(IAppFolderInfo appFolderInfo)
    {
        if (appFolderInfo == null)
        {
            throw new ArgumentNullException(nameof(appFolderInfo));
        }

        _configFile = Path.Combine(appFolderInfo.AppDataFolder, ConfigFileName);
        _config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        LoadFromFile();

        if (string.IsNullOrEmpty(ApiKey))
        {
            SetValue("ApiKey", GenerateApiKey());
        }
    }

    public string BindAddress => GetValue("BindAddress", "*");
    public int Port => GetValueInt("Port", 7889);
    public bool EnableSsl => GetValueBool("EnableSsl", false);
    public string ApiKey => GetValue("ApiKey", string.Empty);
    public bool AuthenticationEnabled => GetValueBool("AuthenticationEnabled", false);
    public string LogLevel => GetValue("LogLevel", "info");
    public string UrlBase => GetValue("UrlBase", string.Empty);
    public string PostgresHost => GetValue("PostgresHost", string.Empty);
    public int PostgresPort => GetValueInt("PostgresPort", 5432);
    public string PostgresMainDb => GetValue("PostgresMainDb", string.Empty);
    public string PostgresUser => GetValue("PostgresUser", string.Empty);
    public string PostgresPassword => GetValue("PostgresPassword", string.Empty);

    private void LoadFromFile()
    {
        lock (Mutex)
        {
            if (!File.Exists(_configFile))
            {
                return;
            }

            var xDoc = XDocument.Load(_configFile);
            var config = xDoc.Element(ConfigElementName);
            if (config == null)
            {
                return;
            }

            foreach (var element in config.Elements())
            {
                _config[element.Name.LocalName] = element.Value.Trim();
            }
        }
    }

    private void SetValue(string key, string value)
    {
        _config[key] = value;
        SaveToFile();
    }

    private void SaveToFile()
    {
        lock (Mutex)
        {
            var configElement = new XElement(ConfigElementName);
            foreach (var kvp in _config)
            {
                configElement.Add(new XElement(kvp.Key, kvp.Value));
            }

            var xDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), configElement);
            xDoc.Save(_configFile);
        }
    }

    private string GetValue(string key, string defaultValue)
    {
        return _config.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private int GetValueInt(string key, int defaultValue)
    {
        var value = GetValue(key, null);
        return value != null && int.TryParse(value, out var result) ? result : defaultValue;
    }

    private bool GetValueBool(string key, bool defaultValue)
    {
        var value = GetValue(key, null);
        return value != null && bool.TryParse(value, out var result) ? result : defaultValue;
    }

    private static string GenerateApiKey()
    {
        return Guid.NewGuid().ToString("N");
    }
}
