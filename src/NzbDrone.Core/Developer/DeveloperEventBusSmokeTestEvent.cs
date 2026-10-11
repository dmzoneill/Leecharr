// Copyright (c) FeedItOut. All rights reserved.

using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Developer;

/// <summary>
/// Lightweight event used by the developer messaging smoke test. Not handled by production startup logic.
/// </summary>
public class DeveloperEventBusSmokeTestEvent : IEvent
{
}
