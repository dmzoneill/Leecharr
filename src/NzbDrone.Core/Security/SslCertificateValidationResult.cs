// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Security;

public class SslCertificateValidationResult
{
    public bool IsValid { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public DateTime ValidFrom { get; set; }

    public DateTime ValidTo { get; set; }

    public string Thumbprint { get; set; } = string.Empty;

    public bool HasPrivateKey { get; set; }

    public List<string> SubjectAlternativeNames { get; set; } = new();

    public bool HandshakeSucceeded { get; set; }

    public string Message { get; set; } = string.Empty;
}
