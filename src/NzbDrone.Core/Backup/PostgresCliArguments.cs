// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace NzbDrone.Core.Backup;

/// <summary>
/// Builds pg_dump and psql argument lists from config.xml connection fields.
/// Each field is one argv element. Values that could split into extra flags are rejected.
/// </summary>
public static class PostgresCliArguments
{
    public static bool TryBuildPgDumpArguments(string host, int port, string user, string dbName, string outputPath, out string[] arguments)
    {
        if (!TryBuildConnectionArguments(host, port, user, dbName, out arguments) || string.IsNullOrEmpty(outputPath))
        {
            arguments = Array.Empty<string>();
            return false;
        }

        var full = new List<string>(arguments.Length + 4)
        {
            "--clean",
            "--if-exists",
        };
        full.AddRange(arguments);
        full.Add("-f");
        full.Add(outputPath);
        arguments = full.ToArray();
        return true;
    }

    public static bool TryBuildPsqlArguments(string host, int port, string user, string dbName, string sqlScriptPath, out string[] arguments)
    {
        if (!TryBuildConnectionArguments(host, port, user, dbName, out arguments) || string.IsNullOrEmpty(sqlScriptPath))
        {
            arguments = Array.Empty<string>();
            return false;
        }

        var full = new List<string>(arguments.Length + 5);
        full.AddRange(arguments);
        full.Add("-v");
        full.Add("ON_ERROR_STOP=1");
        full.Add("--single-transaction");
        full.Add("-f");
        full.Add(sqlScriptPath);
        arguments = full.ToArray();
        return true;
    }

    private static bool TryBuildConnectionArguments(string host, int port, string user, string dbName, out string[] arguments)
    {
        arguments = Array.Empty<string>();
        if (!IsSafeConnectionValue(host) || !IsSafeConnectionValue(dbName))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(user) && !IsSafeConnectionValue(user))
        {
            return false;
        }

        var args = new List<string>
        {
            "-h",
            host,
            "-p",
            port.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(user))
        {
            args.Add("-U");
            args.Add(user);
        }

        args.Add("-d");
        args.Add(dbName);
        arguments = args.ToArray();
        return true;
    }

    private static bool IsSafeConnectionValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] == '-')
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (char.IsControl(ch) || char.IsWhiteSpace(ch) || ch == '"' || ch == '\'' || ch == '\\')
            {
                return false;
            }
        }

        return true;
    }
}
