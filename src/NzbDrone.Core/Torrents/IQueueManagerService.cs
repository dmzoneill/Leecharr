// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Threading.Tasks;

namespace NzbDrone.Core.Torrents;

public interface IQueueManagerService
{
    Task ProcessQueueAsync();
}
