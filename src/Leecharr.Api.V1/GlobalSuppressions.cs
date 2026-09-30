// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Maintainability", "S6964", Justification = "Resource models serve dual purpose for both inbound requests and outbound responses, with existing server state preserved for omitted fields.")]
[assembly: SuppressMessage("csharpsquid", "S6964", Justification = "Resource models serve dual purpose for both inbound requests and outbound responses.")]
[assembly: SuppressMessage("Maintainability", "S6932", Justification = "Fallback request inspection handles multipart forms and header overrides.")]
[assembly: SuppressMessage("csharpsquid", "S6932", Justification = "Fallback request inspection handles multipart forms and header overrides.")]
[assembly: SuppressMessage("Security", "S2077", Scope = "namespaceanddescendants", Target = "Leecharr.Api.V1.System", Justification = "Administrative developer database execution console queries.")]
[assembly: SuppressMessage("csharpsquid", "S2077", Scope = "namespaceanddescendants", Target = "Leecharr.Api.V1.System", Justification = "Administrative developer database execution console queries.")]
