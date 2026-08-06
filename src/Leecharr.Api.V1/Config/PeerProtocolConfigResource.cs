using Leecharr.Http.REST;
using NzbDrone.Core.Configuration;

namespace Leecharr.Api.V1.Config;

public class PeerProtocolConfigResource : RestResource
{
    public int HandshakeTimeoutSeconds { get; set; }
    public int MessageReadTimeoutSeconds { get; set; }
    public int KeepAliveIntervalSeconds { get; set; }
    public int PeerContactIntervalSeconds { get; set; }
    public int UdpTrackerTimeoutSeconds { get; set; }
    public int HttpTrackerTimeoutSeconds { get; set; }
    public int PeerRequestCount { get; set; }
    public double SeederUploadActivityProbability { get; set; }
    public double PeerIdleChance { get; set; }
    public double PeerDropoutProbability { get; set; }
    public double ConnectionRotationPercentage { get; set; }
}

public static class PeerProtocolConfigResourceMapper
{
    public static PeerProtocolConfigResource ToResource(IConfigService model)
    {
        return new PeerProtocolConfigResource
        {
            HandshakeTimeoutSeconds = model.HandshakeTimeoutSeconds,
            MessageReadTimeoutSeconds = model.MessageReadTimeoutSeconds,
            KeepAliveIntervalSeconds = model.KeepAliveIntervalSeconds,
            PeerContactIntervalSeconds = model.PeerContactIntervalSeconds,
            UdpTrackerTimeoutSeconds = model.UdpTrackerTimeoutSeconds,
            HttpTrackerTimeoutSeconds = model.HttpTrackerTimeoutSeconds,
            PeerRequestCount = model.PeerRequestCount,
            SeederUploadActivityProbability = model.SeederUploadActivityProbability,
            PeerIdleChance = model.PeerIdleChance,
            PeerDropoutProbability = model.PeerDropoutProbability,
            ConnectionRotationPercentage = model.ConnectionRotationPercentage,
        };
    }
}
