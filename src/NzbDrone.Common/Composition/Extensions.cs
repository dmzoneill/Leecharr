using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;

namespace NzbDrone.Common.Composition;

public static class ContainerExtensions
{
    public static Rules WithNzbDroneRules(this Rules rules)
    {
        return rules
            .WithAutoConcreteTypeResolution()
            .WithDefaultReuse(Reuse.Singleton)
            .With(Made.Of(FactoryMethod.ConstructorWithResolvableArguments));
    }

    public static void AutoAddServices(this IContainer container, List<string> assemblyNames)
    {
        var assemblies = AssemblyLoader.Load(assemblyNames);
        var types = assemblies.SelectMany(a => a.GetExportedTypes()).ToList();

        KnownTypes.Register(types);

        foreach (var type in types)
        {
            if (type.IsInterface || type.IsAbstract || type.IsEnum || type.IsSubclassOf(typeof(Attribute)))
            {
                continue;
            }

            var interfaces = type.GetInterfaces();
            if (interfaces.Length > 0)
            {
                container.RegisterMany(new[] { type }, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
            }
            else
            {
                container.Register(type, Reuse.Transient, ifAlreadyRegistered: IfAlreadyRegistered.Keep);
            }
        }
    }
}
