using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class AiConfigResource : RestResource
{
    public string ActiveAiProvider { get; set; }
    public string OllamaHost { get; set; }
    public string OllamaModel { get; set; }
    public string GeminiApiKey { get; set; }
    public string GeminiModel { get; set; }
    public string OnnxModelPath { get; set; }
    public bool EnableCopilotButton { get; set; }
    public bool EnableNaturalSearch { get; set; }
    public bool EnableSwarmDiagnostics { get; set; }
}

public static class AiConfigResourceMapper
{
    public static AiConfigResource ToResource(IConfigService config)
    {
        var apiKey = config.GeminiApiKey;
        var maskedApiKey = string.IsNullOrEmpty(apiKey)
            ? string.Empty
            : (apiKey.Length > 4 ? new string('*', apiKey.Length - 4) + apiKey[^4..] : "********");

        return new AiConfigResource
        {
            ActiveAiProvider = config.ActiveAiProvider,
            OllamaHost = config.OllamaHost,
            OllamaModel = config.OllamaModel,
            GeminiApiKey = maskedApiKey,
            GeminiModel = config.GeminiModel,
            OnnxModelPath = config.OnnxModelPath,
            EnableCopilotButton = config.EnableCopilotButton,
            EnableNaturalSearch = config.EnableNaturalSearch,
            EnableSwarmDiagnostics = config.EnableSwarmDiagnostics
        };
    }
}
