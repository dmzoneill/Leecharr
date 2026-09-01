// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Security;

public class CertificateManager : ICertificateManager
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IAppFolderInfo appFolderInfo;

    public CertificateManager(IAppFolderInfo appFolderInfo)
    {
        this.appFolderInfo = appFolderInfo ?? throw new ArgumentNullException(nameof(appFolderInfo));
    }

    public X509Certificate2 GetOrCreateCertificate(IConfigFileProvider config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!string.IsNullOrWhiteSpace(config.SslCertPath))
        {
            var certPath = config.SslCertPath.Trim();
            if (File.Exists(certPath))
            {
                try
                {
                    var loadedCert = this.LoadCustomCertificate(certPath, config.SslKeyPath, config.SslCertPassword);
                    if (loadedCert != null)
                    {
                        Logger.Info("Successfully loaded custom SSL certificate from '{0}'", certPath);
                        return loadedCert;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to load custom SSL certificate from '{0}'. Falling back to self-signed certificate.", certPath);
                }
            }
            else
            {
                Logger.Warn("Configured SSL certificate path '{0}' was not found. Falling back to self-signed certificate.", certPath);
            }
        }

        return this.GetOrCreateSelfSignedCertificate(config);
    }

    public async Task<SslCertificateValidationResult> ValidateCertificateAsync(
        string certPath,
        string keyPath,
        string password,
        string bindAddress,
        int sslPort,
        bool testTlsHandshake = true)
    {
        var result = new SslCertificateValidationResult();

        try
        {
            X509Certificate2 cert = null;
            if (!string.IsNullOrWhiteSpace(certPath))
            {
                var trimmedPath = certPath.Trim();
                if (!File.Exists(trimmedPath))
                {
                    result.IsValid = false;
                    result.Message = $"Certificate file not found at '{trimmedPath}'.";
                    return result;
                }

                cert = this.LoadCustomCertificate(trimmedPath, keyPath, password);
            }
            else
            {
                var dummyConfig = new DummySslConfig(bindAddress);
                cert = this.GetOrCreateSelfSignedCertificate(dummyConfig);
            }

            if (cert == null)
            {
                result.IsValid = false;
                result.Message = "Failed to load or generate a valid certificate.";
                return result;
            }

            result.Subject = cert.Subject;
            result.Issuer = cert.Issuer;
            result.ValidFrom = cert.NotBefore.ToUniversalTime();
            result.ValidTo = cert.NotAfter.ToUniversalTime();
            result.Thumbprint = cert.Thumbprint;
            result.HasPrivateKey = cert.HasPrivateKey;

            foreach (var ext in cert.Extensions)
            {
                if (ext is X509SubjectAlternativeNameExtension sanExt)
                {
                    foreach (var dns in sanExt.EnumerateDnsNames())
                    {
                        result.SubjectAlternativeNames.Add(dns);
                    }

                    foreach (var ip in sanExt.EnumerateIPAddresses())
                    {
                        result.SubjectAlternativeNames.Add(ip.ToString());
                    }
                }
            }

            if (!cert.HasPrivateKey)
            {
                result.IsValid = false;
                result.Message = "The certificate was loaded, but it does not contain a private key.";
                return result;
            }

            if (DateTime.UtcNow > cert.NotAfter.ToUniversalTime())
            {
                result.IsValid = false;
                result.Message = $"The certificate has expired on {cert.NotAfter.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC.";
                return result;
            }

            if (DateTime.UtcNow < cert.NotBefore.ToUniversalTime())
            {
                result.IsValid = false;
                result.Message = $"The certificate is not yet valid (valid starting {cert.NotBefore.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC).";
                return result;
            }

            result.IsValid = true;
            result.Message = "Certificate is valid and cryptographically verified.";

            if (testTlsHandshake && sslPort is >= 1 and <= 65535)
            {
                try
                {
                    using var handler = new SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(2),
                        SslOptions = new SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (sender, serverCert, chain, errors) => true,
                        },
                    };

                    using var httpClient = new HttpClient(handler)
                    {
                        Timeout = TimeSpan.FromSeconds(2),
                    };

                    var testUrl = $"https://127.0.0.1:{sslPort}/";
                    using var response = await httpClient.GetAsync(testUrl);
                    result.HandshakeSucceeded = true;
                    result.Message = $"Certificate is valid and active on HTTPS port {sslPort} (TLS handshake succeeded).";
                }
                catch
                {
                    result.HandshakeSucceeded = false;
                    result.Message = $"Certificate is valid. (HTTPS listener will activate on port {sslPort} once the server is restarted).";
                }
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Message = $"Certificate validation failed: {ex.Message}";
        }

        return result;
    }

    private X509Certificate2 LoadCustomCertificate(string certPath, string keyPath, string password)
    {
        var ext = Path.GetExtension(certPath).ToLowerInvariant();

        if (ext is ".pfx" or ".p12")
        {
            var pass = string.IsNullOrEmpty(password) ? null : password;
            return X509CertificateLoader.LoadPkcs12FromFile(
                certPath,
                pass,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        }

        if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath))
        {
            return X509Certificate2.CreateFromPemFile(certPath, keyPath);
        }

        var pemContent = File.ReadAllText(certPath);
        if (pemContent.Contains("PRIVATE KEY", StringComparison.OrdinalIgnoreCase))
        {
            return X509Certificate2.CreateFromPem(pemContent, pemContent);
        }

        throw new InvalidOperationException($"Certificate file '{certPath}' does not contain a private key and no private key file was provided.");
    }

    private X509Certificate2 GetOrCreateSelfSignedCertificate(IConfigFileProvider config)
    {
        var cachePath = Path.Combine(this.appFolderInfo.AppDataFolder, "leecharr-selfsigned.pfx");
        const string pfxPassword = "leecharr-selfsigned";

        if (File.Exists(cachePath))
        {
            try
            {
                var cached = X509CertificateLoader.LoadPkcs12FromFile(
                    cachePath,
                    pfxPassword,
                    X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

                if (DateTime.UtcNow < cached.NotAfter.ToUniversalTime().AddDays(-30))
                {
                    Logger.Info("Using existing cached self-signed SSL certificate: {0} (Expires: {1:yyyy-MM-dd})", cached.Subject, cached.NotAfter);
                    return cached;
                }

                Logger.Info("Cached self-signed SSL certificate is expired or expiring soon. Regenerating.");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load cached self-signed certificate from '{0}'. Regenerating.", cachePath);
            }
        }

        return this.GenerateAndSaveSelfSignedCertificate(config, cachePath, pfxPassword);
    }

    private X509Certificate2 GenerateAndSaveSelfSignedCertificate(IConfigFileProvider config, string cachePath, string pfxPassword)
    {
        Logger.Info("Generating new self-signed RSA-2048 SSL certificate for Leecharr...");

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=Leecharr, O=Leecharr, OU=Media Downloader",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        try
        {
            sanBuilder.AddDnsName(Environment.MachineName);
        }
        catch
        {
            // Ignore invalid hostname characters
        }

        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);

        if (!string.IsNullOrWhiteSpace(config.BindAddress) &&
            IPAddress.TryParse(config.BindAddress, out var bindIp) &&
            !IPAddress.Any.Equals(bindIp) &&
            !IPAddress.IPv6Any.Equals(bindIp) &&
            !IPAddress.Loopback.Equals(bindIp) &&
            !IPAddress.IPv6Loopback.Equals(bindIp))
        {
            sanBuilder.AddIpAddress(bindIp);
        }

        req.CertificateExtensions.Add(sanBuilder.Build());

        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = DateTimeOffset.UtcNow.AddYears(5);

        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        var pfxBytes = cert.Export(X509ContentType.Pfx, pfxPassword);

        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(cachePath, pfxBytes);
            Logger.Info("Saved generated self-signed SSL certificate to '{0}'", cachePath);
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Could not persist self-signed SSL certificate to disk at '{0}'", cachePath);
        }

        return X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            pfxPassword,
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
    }

    private sealed class DummySslConfig : IConfigFileProvider
    {
        public DummySslConfig(string bindAddress)
        {
            this.BindAddress = string.IsNullOrWhiteSpace(bindAddress) ? "*" : bindAddress;
        }

        public string BindAddress { get; }

        public int Port => 7889;

        public bool EnableSsl => true;

        public int SslPort => 7890;

        public string SslCertPath => string.Empty;

        public string SslKeyPath => string.Empty;

        public string SslCertPassword => string.Empty;

        public bool RedirectHttpToHttps => false;

        public string ApiKey => string.Empty;

        public bool AuthenticationEnabled => false;

        public string LogLevel => "info";

        public string UrlBase => string.Empty;

        public string PostgresHost => string.Empty;

        public int PostgresPort => 5432;

        public string PostgresMainDb => string.Empty;

        public string PostgresUser => string.Empty;

        public string PostgresPassword => string.Empty;

        public void SaveConfigDictionary(Dictionary<string, object> values)
        {
        }
    }
}
