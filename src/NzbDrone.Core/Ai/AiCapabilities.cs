// Copyright (c) PlaceholderCompany. All rights reserved.

using System;

namespace NzbDrone.Core.Ai;

[Flags]
public enum AiCapabilities
{
    None = 0,
    SupportsNaturalLanguageSearch = 1 << 0,
    SupportsReleaseNameParsing = 1 << 1,
    SupportsDiagnosticCopilot = 1 << 2,
    SupportsMalwareAnomalyDetection = 1 << 3,
    SupportsSwarmOptimization = 1 << 4,
    SupportsLocalOfflineInference = 1 << 5,
    SupportsCloudLlm = 1 << 6,
    All = SupportsNaturalLanguageSearch |
          SupportsReleaseNameParsing |
          SupportsDiagnosticCopilot |
          SupportsMalwareAnomalyDetection |
          SupportsSwarmOptimization |
          SupportsLocalOfflineInference |
          SupportsCloudLlm,
}
