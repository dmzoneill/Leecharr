// Copyright (c) FeedItOut. All rights reserved.

using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Leecharr.Api.V1.SignalR;

public interface ITorrentHubBroadcaster
{
    Task BroadcastTorrentProgress(int torrentId, object progressPayload);

    Task BroadcastGlobalProgress(object progressPayload);

    Task PushSwarmTelemetry(int torrentId, object swarmData);

    Task BroadcastSwarmTelemetry(object swarmData);
}

public class TorrentHubBroadcaster : ITorrentHubBroadcaster
{
    private readonly IHubContext<TorrentHub> hubContext;

    public TorrentHubBroadcaster(IHubContext<TorrentHub> hubContext)
    {
        this.hubContext = hubContext;
    }

    public Task BroadcastTorrentProgress(int torrentId, object progressPayload)
    {
        return this.hubContext.Clients.Group($"torrent-{torrentId}").SendAsync("torrentProgress", progressPayload);
    }

    public Task BroadcastGlobalProgress(object progressPayload)
    {
        return this.hubContext.Clients.Group("torrents").SendAsync("torrentProgress", progressPayload);
    }

    public Task PushSwarmTelemetry(int torrentId, object swarmData)
    {
        return this.hubContext.Clients.Group($"torrent-{torrentId}").SendAsync("swarmTelemetry", swarmData);
    }

    public Task BroadcastSwarmTelemetry(object swarmData)
    {
        return this.hubContext.Clients.All.SendAsync("swarmTelemetry", swarmData);
    }
}
