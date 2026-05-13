namespace NzbDrone.Core.Messaging.Events;

public interface IHandle<TEvent>
    where TEvent : class, IEvent
{
    void Handle(TEvent message);
}
