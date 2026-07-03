using System;
using System.Collections.Generic;
using System.IO;
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
            var path = Path.Combine(baseDir, $"{name}.dll");
            if (File.Exists(path))
            {
                assemblies.Add(Assembly.LoadFrom(path));
            }
            else
            {
                try
                {
                    assemblies.Add(Assembly.Load(name));
                }
                catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
                {
                    Logger.Warn(ex, "Could not load assembly {0}", name);
                }
            }
        }

        return assemblies;
    }
}
