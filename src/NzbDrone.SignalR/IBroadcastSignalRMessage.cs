namespace NzbDrone.SignalR;

public interface IBroadcastSignalRMessage
{
    bool IsConnected { get; }
    void BroadcastMessage(SignalRMessage message);
}
