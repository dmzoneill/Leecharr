using System;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;

namespace Leecharr.Http.REST;

public abstract class RestControllerWithSignalR<TResource, TModel> : RestController<TResource>, IHandle<ModelEvent<TModel>>
    where TResource : RestResource, new()
    where TModel : ModelBase, new()
{
    private readonly IBroadcastSignalRMessage _signalRBroadcaster;

    protected RestControllerWithSignalR(IBroadcastSignalRMessage signalRBroadcaster)
    {
        _signalRBroadcaster = signalRBroadcaster;
    }

    public void Handle(ModelEvent<TModel> message)
    {
        if (message == null || !_signalRBroadcaster.IsConnected)
        {
            return;
        }

        var resource = GetResourceById(message.Model);
        if (resource == null)
        {
            return;
        }

        BroadcastResourceChange(message.Action, resource);
    }

    protected virtual TResource GetResourceById(TModel model)
    {
        throw new NotImplementedException($"{GetType().Name} must override GetResourceById");
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
            Action = action
        };

        _signalRBroadcaster.BroadcastMessage(signalRMessage);
    }
}
