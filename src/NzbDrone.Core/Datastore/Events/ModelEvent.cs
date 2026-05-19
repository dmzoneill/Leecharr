using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Datastore.Events;

public class ModelEvent<TModel> : IEvent
{
    public ModelEvent(TModel model, ModelAction action)
    {
        Model = model;
        Action = action;
    }

    public TModel Model { get; set; }
    public ModelAction Action { get; set; }
}
