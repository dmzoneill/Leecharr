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

    public static void RegisterSingletonWithInterfaces<TImplementation>(
        this IContainer container,
        IfAlreadyRegistered ifAlreadyRegistered = IfAlreadyRegistered.Replace)
        where TImplementation : class
    {
        container.RegisterSingletonWithInterfaces(typeof(TImplementation), ifAlreadyRegistered);
    }

    public static void RegisterSingletonWithInterfaces(
        this IContainer container,
        Type implementationType,
        IfAlreadyRegistered ifAlreadyRegistered = IfAlreadyRegistered.Replace)
    {
        container.Register(implementationType, Reuse.Singleton, ifAlreadyRegistered: ifAlreadyRegistered);

        var interfaces = implementationType.GetInterfaces()
            .Where(i => i != typeof(IDisposable) && i != typeof(IAsyncDisposable))
            .ToArray();

        foreach (var iface in interfaces)
        {
            container.RegisterMapping(iface, implementationType, ifAlreadyRegistered: ifAlreadyRegistered);
        }
    }

    public static void RegisterSingleton<TService, TImplementation>(
        this IContainer container,
        IfAlreadyRegistered ifAlreadyRegistered = IfAlreadyRegistered.Replace)
        where TImplementation : class, TService
    {
        container.Register<TImplementation>(Reuse.Singleton, ifAlreadyRegistered: ifAlreadyRegistered);
        if (typeof(TService) != typeof(TImplementation))
        {
            container.RegisterMapping<TService, TImplementation>(ifAlreadyRegistered: ifAlreadyRegistered);
        }
    }

    public static void AutoAddServices(this IContainer container, List<string> assemblyNames)
    {
        var assemblies = AssemblyLoader.Load(assemblyNames);
        var types = assemblies.SelectMany(a => a.GetExportedTypes()).ToList();

        KnownTypes.Register(types);

        foreach (var type in types)
        {
            if (type.IsInterface || type.IsAbstract || type.IsEnum || type.IsValueType || type.IsSubclassOf(typeof(Attribute)) ||
                type.Name.EndsWith("Event") || type.Name.EndsWith("Command") || type.Name.EndsWith("Resource") ||
                type.Name == "Database" ||
                type.GetInterfaces().Any(i => i.Name == "IEvent" || i.Name == "IDownloadTask" || i.Name == "ITerminalSession") ||
                (type.BaseType != null && (type.BaseType.Name == "ModelBase" || type.BaseType.Name == "Command" || type.BaseType.Name == "RestResource")))
            {
                continue;
            }

            var interfaces = type.GetInterfaces()
                .Where(i => i != typeof(IDisposable) && i != typeof(IAsyncDisposable))
                .ToArray();

            if (type.Name.EndsWith("Controller") || (type.BaseType != null && (type.BaseType.Name == "ControllerBase" || type.BaseType.Name == "Controller")))
            {
                var handleInterfaces = interfaces.Where(i => i.IsGenericType && i.Name.StartsWith("IHandle`1")).ToArray();
                foreach (var hi in handleInterfaces)
                {
                    container.Register(hi, type, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
                }

                continue;
            }

            if (type.IsGenericTypeDefinition)
            {
                container.RegisterMany(new[] { type }, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
            }
            else if (interfaces.Length > 0)
            {
                // Register concrete type as Singleton first
                container.Register(type, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Keep);

                // Map all interfaces to the concrete singleton instance
                foreach (var iface in interfaces)
                {
                    container.RegisterMapping(iface, type, ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
                }
            }
            else
            {
                // Concrete classes without interfaces must also be registered as Singleton to prevent duplicate stateful instances
                container.Register(type, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Keep);
            }
        }
    }
}
