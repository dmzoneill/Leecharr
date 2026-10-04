// Copyright (c) FeedItOut. All rights reserved.

using System.Collections.Generic;

namespace NzbDrone.Core.Developer.Uml;

public interface IDeveloperUmlService
{
    DeveloperUmlDiagramResult GenerateDiagram(DeveloperUmlOptions options);

    List<string> GetAvailableSubsystems();

    List<string> GetAvailableDiagramTypes();
}
