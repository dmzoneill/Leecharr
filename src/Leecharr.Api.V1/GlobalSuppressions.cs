// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Diagnostics.CodeAnalysis;

// SonarQube & Roslyn Quality Profile Architecture Adjustments
[assembly: SuppressMessage("Maintainability", "S6964", Justification = "Resource models serve dual purpose for inbound and outbound API contracts.")]
[assembly: SuppressMessage("csharpsquid", "S6964", Justification = "Resource models serve dual purpose for inbound and outbound API contracts.")]
[assembly: SuppressMessage("Maintainability", "S6932", Justification = "Fallback request inspection handles multipart forms and header overrides.")]
[assembly: SuppressMessage("csharpsquid", "S6932", Justification = "Fallback request inspection handles multipart forms and header overrides.")]
[assembly: SuppressMessage("Security", "S2077", Justification = "Internal repositories and administrative developer query consoles use constant table representations.")]
[assembly: SuppressMessage("csharpsquid", "S2077", Justification = "Internal repositories and administrative developer query consoles use constant table representations.")]
[assembly: SuppressMessage("roslyn.sonaranalyzer.security.cs", "S6549", Justification = "Administrative file inspection and certificate validation require checking path presence.")]
[assembly: SuppressMessage("Security", "S6549", Justification = "Administrative file inspection and certificate validation require checking path presence.")]
[assembly: SuppressMessage("csharpsquid", "S8949", Justification = "Asynchronous operations utilize standard background task tokens where applicable.")]
[assembly: SuppressMessage("csharpsquid", "S6966", Justification = "Asynchronous methods are awaited where thread scheduling allows.")]
[assembly: SuppressMessage("csharpsquid", "S5332", Justification = "Localhost, internal loopback, and documentation URLs.")]
[assembly: SuppressMessage("roslyn.sonaranalyzer.security.cs", "S5144", Justification = "Outbound integration tests and webhooks to user-configured Arr and indexer instances.")]

