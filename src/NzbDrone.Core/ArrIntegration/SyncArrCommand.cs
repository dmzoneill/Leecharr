// Copyright (c) PlaceholderCompany. All rights reserved.

using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.ArrIntegration;

public class SyncArrCommand : Command
{
    public string AppType { get; set; }

    public int? InstanceId { get; set; }
}
