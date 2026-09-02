// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DownloadClients;

public interface IDownloadClientRepository : IBasicRepository<DownloadClientDefinition>
{
    IEnumerable<DownloadClientDefinition> GetEnabled();

    DownloadClientDefinition GetByType(string clientType);
}
