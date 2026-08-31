// Copyright (c) PlaceholderCompany. All rights reserved.

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
            if (type.IsInterface || type.IsAbstract || type.IsEnum || type.IsSubclassOf(typeof(Attribute)) ||
                type.Name.EndsWith("Event") || type.Name.EndsWith("Command") || type.Name.EndsWith("Resource") ||
                type.GetInterfaces().Any(i => i.Name == "IEvent" || i.Name == "IDownloadTask") ||
                (type.BaseType != null && (type.BaseType.Name == "ModelBase" || type.BaseType.Name == "Command" || type.BaseType.Name == "RestResource")))
            {
                continue;
            }

            var interfaces = type.GetInterfaces();
            if (type.Name.EndsWith("Controller") || (type.BaseType != null && (type.BaseType.Name == "ControllerBase" || type.BaseType.Name == "Controller")))
            {
                var handleInterfaces = interfaces.Where(i => i.IsGenericType && i.Name.StartsWith("IHandle`1")).ToArray();
                foreach (var hi in handleInterfaces)
                {
                    container.Register(hi, type, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
                }

                continue;
            }

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
