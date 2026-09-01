// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Security;

public interface ICertificateManager
{
    X509Certificate2 GetOrCreateCertificate(IConfigFileProvider config);

    Task<SslCertificateValidationResult> ValidateCertificateAsync(
        string certPath,
        string keyPath,
        string password,
        string bindAddress,
        int sslPort,
        bool testTlsHandshake = true);
}
