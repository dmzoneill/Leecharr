// Copyright (c) FeedItOut. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace NzbDrone.Core.Messaging.Events;

[SuppressMessage("Design", "CA1040:Avoid empty interfaces", Justification = "Marker interface for event aggregator messaging")]
public interface IEvent
{
}
