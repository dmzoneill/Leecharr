// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Common.Composition;

public static class KnownTypes
{
    private static readonly List<Type> Types = new();

    public static void Register(List<Type> newTypes)
    {
        lock (Types)
        {
            Types.AddRange(newTypes);
        }
    }

    public static List<Type> GetImplementations(Type contractType)
    {
        lock (Types)
        {
            return Types
                .Where(t => contractType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
                .ToList();
        }
    }
}
