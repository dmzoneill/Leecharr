// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NLog;

namespace NzbDrone.Common.Composition;

public static class AssemblyLoader
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public static List<Assembly> Load(List<string> names)
    {
        var assemblies = new List<Assembly>();
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var name in names)
        {
            TryAddAssembly(assemblies, name, Path.Combine(baseDir, $"{name}.dll"));
        }

        return assemblies;
    }

    public static IEnumerable<Type> GetExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return GetLoadableTypes(assembly, ex);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            Logger.Warn(ex, "Could not load exported types from assembly {0}", assembly.FullName);
            return GetLoadableExportedTypes(assembly);
        }
    }

    private static IEnumerable<Type> GetLoadableExportedTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes().Where(IsExportedType);
        }
        catch (ReflectionTypeLoadException ex)
        {
            return GetLoadableTypes(assembly, ex).Where(IsExportedType);
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            Logger.Warn(ex, "Could not load types from assembly {0}", assembly.FullName);
            return Array.Empty<Type>();
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, ReflectionTypeLoadException ex)
    {
        Logger.Warn(ex, "Could not load all exported types from assembly {0}", assembly.FullName);

        foreach (var loaderException in ex.LoaderExceptions)
        {
            if (loaderException != null)
            {
                Logger.Warn(loaderException, "Type load failure in assembly {0}", assembly.FullName);
            }
        }

        return ex.Types.Where(type => type != null);
    }

    private static bool IsExportedType(Type type)
    {
        return type.IsPublic && !type.IsNested;
    }

    private static void TryAddAssembly(List<Assembly> assemblies, string name, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                assemblies.Add(Assembly.LoadFrom(path));
            }
            else
            {
                assemblies.Add(Assembly.Load(name));
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
        {
            Logger.Warn(ex, "Could not load assembly {0}", name);
        }
    }
}
