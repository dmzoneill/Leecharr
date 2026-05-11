using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Common.Composition;

public static class KnownTypes
{
    private static readonly List<Type> _types = new();

    public static void Register(List<Type> types)
    {
        _types.AddRange(types);
    }

    public static List<Type> GetImplementations(Type contractType)
    {
        return _types
            .Where(t => contractType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();
    }
}
