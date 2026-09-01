// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Leecharr.Http.REST;

public abstract class RestControllerWithSignalR<TResource, TModel> : RestController<TResource>, IHandle<ModelEvent<TModel>>
    where TResource : RestResource, new()
    where TModel : ModelBase, new()
{
    private readonly IBroadcastSignalRMessage signalRBroadcaster;

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster)
    {
        this.signalRBroadcaster = signalRBroadcaster;
    }

    [NonAction]
    public void Handle(ModelEvent<TModel> message)
    {
        if (message == null || !this.signalRBroadcaster.IsConnected)
        {
            return;
        }

        var resource = this.GetResourceById(message.Model);
        if (resource == null)
        {
            return;
        }

        this.BroadcastResourceChange(message.Action, resource);
    }

    protected virtual TResource GetResourceById(TModel model)
    {
        throw new NotImplementedException($"{this.GetType().Name} must override GetResourceById");
    }

    protected void BroadcastResourceChange(ModelAction action, TResource resource)
    {
        if (resource == null)
        {
            return;
        }

        var signalRMessage = new SignalRMessage
        {
            Name = resource.ResourceName,
            Body = resource,
            Action = action,
        };

        this.signalRBroadcaster.BroadcastMessage(signalRMessage);
    }
}
