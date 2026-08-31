// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ArrIntegration;

public interface IArrConnectionRepository : IBasicRepository<ArrConnectionDefinition>
{
    IEnumerable<ArrConnectionDefinition> GetEnabled();

    ArrConnectionDefinition GetByType(string arrType);
}
