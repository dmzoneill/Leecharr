// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

namespace NzbDrone.Common.EnvironmentInfo;

public class StartupContext
{
    public StartupContext(params string[] args)
    {
        this.Flags = new HashSet<string>();
        this.Args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (args == null)
        {
            return;
        }

        foreach (var arg in args)
        {
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            var cleanArg = arg;
            if (cleanArg.StartsWith("--"))
            {
                cleanArg = cleanArg.Substring(2);
            }
            else if (cleanArg.StartsWith("-"))
            {
                cleanArg = cleanArg.Substring(1);
            }

            var parts = cleanArg.Split('=', 2);
            if (parts.Length == 2)
            {
                this.Args[parts[0].ToLowerInvariant()] = parts[1];
            }
            else
            {
                this.Flags.Add(parts[0].ToLowerInvariant());
            }
        }
    }

    public HashSet<string> Flags { get; }

    public Dictionary<string, string> Args { get; }
}
