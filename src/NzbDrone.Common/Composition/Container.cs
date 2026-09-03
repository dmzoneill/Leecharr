// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using DryIoc;

namespace NzbDrone.Common.Composition;

public static class ContainerBuilder
{
    public static IContainer Build(List<string> assemblyNames = null)
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        if (assemblyNames != null && assemblyNames.Count > 0)
        {
            container.AutoAddServices(assemblyNames);
        }

        return container;
    }
}
