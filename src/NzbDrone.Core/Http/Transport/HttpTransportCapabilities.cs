// Copyright (c) PlaceholderCompany. All rights reserved.

namespace NzbDrone.Core.Http.Transport;

public class HttpTransportCapabilities
{
    public bool SupportsHttp3Quic { get; set; }

    public bool SupportsBrowserFingerprintEmulation { get; set; }

    public bool SupportsFlareSolverr { get; set; }

    public bool SupportsCustomProxy { get; set; }

    public bool SupportsTlsJa3Ja4Fingerprinting { get; set; }

    public bool SupportsCookieExtraction { get; set; }
}
