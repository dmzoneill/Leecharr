using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class SimulationConfigResource : RestResource
{
    public bool ClientBehaviorEngineEnabled { get; set; }
    public string PrimaryClient { get; set; }
    public double BehaviorVariation { get; set; }
    public bool ClientProfileSwitching { get; set; }
    public double SwitchClientProbability { get; set; }
    public string TrafficPatternProfile { get; set; }
    public bool RealisticVariations { get; set; }
    public bool TimeBasedPatterns { get; set; }
    public bool SwarmIntelligenceEnabled { get; set; }
    public double SwarmAdaptationRate { get; set; }
    public int SwarmPeerAnalysisDepth { get; set; }
}

public static class SimulationConfigResourceMapper
{
    public static SimulationConfigResource ToResource(IConfigService model)
    {
        return new SimulationConfigResource
        {
            ClientBehaviorEngineEnabled = model.ClientBehaviorEngineEnabled,
            PrimaryClient = model.PrimaryClient,
            BehaviorVariation = model.BehaviorVariation,
            ClientProfileSwitching = model.ClientProfileSwitching,
            SwitchClientProbability = model.SwitchClientProbability,
            TrafficPatternProfile = model.TrafficPatternProfile,
            RealisticVariations = model.RealisticVariations,
            TimeBasedPatterns = model.TimeBasedPatterns,
            SwarmIntelligenceEnabled = model.SwarmIntelligenceEnabled,
            SwarmAdaptationRate = model.SwarmAdaptationRate,
            SwarmPeerAnalysisDepth = model.SwarmPeerAnalysisDepth,
        };
    }
}
