// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Datastore.Events;

public class ModelEvent<TModel> : IEvent
{
    public ModelEvent(TModel model, ModelAction action)
    {
        this.Model = model;
        this.Action = action;
    }

    public TModel Model { get; set; }

    public ModelAction Action { get; set; }
}
