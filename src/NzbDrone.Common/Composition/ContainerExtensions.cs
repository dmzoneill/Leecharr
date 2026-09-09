// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;

namespace NzbDrone.Common.Composition;

public static class ContainerExtensions
{
    private static readonly HashSet<string> ExcludedTypeSuffixes = new(StringComparer.Ordinal)
    {
        "Event",
        "Command",
        "Resource",
    };

    private static readonly HashSet<string> ExcludedTypeNames = new(StringComparer.Ordinal)
    {
        "Database",
    };

    private static readonly HashSet<string> ExcludedInterfaceNames = new(StringComparer.Ordinal)
    {
        "IEvent",
        "IDownloadTask",
        "ITerminalSession",
    };

    private static readonly HashSet<string> ExcludedBaseTypeNames = new(StringComparer.Ordinal)
    {
        "ModelBase",
        "Command",
        "RestResource",
    };

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

        var interfaces = GetServiceInterfaces(implementationType);

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
            if (ShouldSkipType(type))
            {
                continue;
            }

            var interfaces = GetServiceInterfaces(type);

            if (IsController(type))
            {
                RegisterControllerHandlers(container, type, interfaces);
                continue;
            }

            RegisterService(container, type, interfaces);
        }
    }

    private static bool ShouldSkipType(Type type)
    {
        if (type.IsInterface || type.IsAbstract || type.IsEnum || type.IsValueType || type.IsSubclassOf(typeof(Attribute)))
        {
            return true;
        }

        if (ExcludedTypeNames.Contains(type.Name))
        {
            return true;
        }

        foreach (var suffix in ExcludedTypeSuffixes)
        {
            if (type.Name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (type.BaseType != null && ExcludedBaseTypeNames.Contains(type.BaseType.Name))
        {
            return true;
        }

        if (type.GetInterfaces().Any(i => ExcludedInterfaceNames.Contains(i.Name)))
        {
            return true;
        }

        return false;
    }

    private static Type[] GetServiceInterfaces(Type type)
    {
        return type.GetInterfaces()
            .Where(i => i != typeof(IDisposable) && i != typeof(IAsyncDisposable))
            .ToArray();
    }

    private static bool IsController(Type type)
    {
        return type.Name.EndsWith("Controller", StringComparison.Ordinal) ||
               (type.BaseType != null && (type.BaseType.Name == "ControllerBase" || type.BaseType.Name == "Controller"));
    }

    private static void RegisterControllerHandlers(IContainer container, Type type, Type[] interfaces)
    {
        var handleInterfaces = interfaces.Where(i => i.IsGenericType && i.Name.StartsWith("IHandle`1", StringComparison.Ordinal)).ToArray();
        foreach (var hi in handleInterfaces)
        {
            container.Register(hi, type, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.AppendNotKeyed);
        }
    }

    private static void RegisterService(IContainer container, Type type, Type[] interfaces)
    {
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
