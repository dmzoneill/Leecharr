// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration;

public class ConfigFileProvider : IConfigFileProvider
{
    private const string ConfigFileName = "config.xml";
    private const string ConfigElementName = "Config";

    private readonly string configFile;
    private readonly Dictionary<string, string> config;
    private readonly IEventAggregator eventAggregator;
    private static readonly object Mutex = new();

    public ConfigFileProvider(IAppFolderInfo appFolderInfo, IEventAggregator eventAggregator = null)
    {
        if (appFolderInfo == null)
        {
            throw new ArgumentNullException(nameof(appFolderInfo));
        }

        this.eventAggregator = eventAggregator;
        this.configFile = Path.Combine(appFolderInfo.AppDataFolder, ConfigFileName);
        this.config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        this.LoadFromFile();

        if (string.IsNullOrEmpty(this.ApiKey))
        {
            this.config["ApiKey"] = GenerateApiKey();
            this.SaveToFile();
        }
    }

    public string BindAddress => this.GetValue("BindAddress", "*");

    public int Port => this.GetValueInt("Port", 7889);

    public bool EnableSsl => this.GetValueBool("EnableSsl", false);

    public int SslPort => this.GetValueInt("SslPort", 7890);

    public string SslCertPath => this.GetValue("SslCertPath", string.Empty);

    public string SslKeyPath => this.GetValue("SslKeyPath", string.Empty);

    public string SslCertPassword => this.GetValue("SslCertPassword", string.Empty);

    public bool RedirectHttpToHttps => this.GetValueBool("RedirectHttpToHttps", false);

    public string ApiKey => this.GetValue("ApiKey", string.Empty);

    public bool AuthenticationEnabled => this.GetValueBool("AuthenticationEnabled", false);

    public string LogLevel => this.GetValue("LogLevel", "info");

    public string UrlBase => this.GetValue("UrlBase", string.Empty);

    public string PostgresHost => this.GetValue("PostgresHost", string.Empty);

    public int PostgresPort => this.GetValueInt("PostgresPort", 5432);

    public string PostgresMainDb => this.GetValue("PostgresMainDb", string.Empty);

    public string PostgresUser => this.GetValue("PostgresUser", string.Empty);

    public string PostgresPassword => this.GetValue("PostgresPassword", string.Empty);

    private void LoadFromFile()
    {
        lock (Mutex)
        {
            if (!File.Exists(this.configFile))
            {
                return;
            }

            var xDoc = XDocument.Load(this.configFile);
            var config = xDoc.Element(ConfigElementName);
            if (config == null)
            {
                return;
            }

            foreach (var element in config.Elements())
            {
                this.config[element.Name.LocalName] = element.Value.Trim();
            }
        }
    }

    private void SetValue(string key, string value)
    {
        this.config[key] = value;
        this.SaveToFile();
        this.eventAggregator?.PublishEvent(new ConfigFileSavedEvent());
    }

    public void SaveConfigDictionary(Dictionary<string, object> values)
    {
        if (values == null)
        {
            return;
        }

        lock (Mutex)
        {
            foreach (var (key, value) in values)
            {
                if (value != null)
                {
                    this.config[key] = value.ToString();
                }
            }

            this.SaveToFile();
        }

        this.eventAggregator?.PublishEvent(new ConfigFileSavedEvent());
    }

    private void SaveToFile()
    {
        lock (Mutex)
        {
            var configElement = new XElement(ConfigElementName);
            foreach (var kvp in this.config)
            {
                configElement.Add(new XElement(kvp.Key, kvp.Value));
            }

            var xDoc = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), configElement);
            xDoc.Save(this.configFile);
        }
    }

    private string GetValue(string key, string defaultValue)
    {
        return this.config.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private int GetValueInt(string key, int defaultValue)
    {
        var value = this.GetValue(key, null);
        return value != null && int.TryParse(value, out var result) ? result : defaultValue;
    }

    private bool GetValueBool(string key, bool defaultValue)
    {
        var value = this.GetValue(key, null);
        return value != null && bool.TryParse(value, out var result) ? result : defaultValue;
    }

    private static string GenerateApiKey()
    {
        return Guid.NewGuid().ToString("N");
    }
}
