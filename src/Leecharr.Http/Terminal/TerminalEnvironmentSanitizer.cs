// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NzbDrone.Common.Disk;

namespace Leecharr.Http.Terminal;

public static class TerminalEnvironmentSanitizer
{
    private static readonly string[] SensitivePatterns =
    [
        "PASSWORD",
        "SECRET",
        "POSTGRES",
        "API_KEY",
        "TOKEN",
        "DATABASE_URL",
        "JWT",
        "AUTH",
        "CREDENTIAL",
        "PRIVATE_KEY",
        "LEECHARR_",
        "ACCESS_KEY",
        "CONNECTIONSTRING",
        "LD_PRELOAD",
        "LD_LIBRARY_PATH",
        "LD_AUDIT",
    ];

    public static bool IsSensitiveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (var pattern in SensitivePatterns)
        {
            if (key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static void StripSensitiveKeys(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var sensitiveKeys = startInfo.EnvironmentVariables.Keys.Cast<string>()
            .Where(IsSensitiveKey)
            .ToList();

        foreach (var key in sensitiveKeys)
        {
            startInfo.EnvironmentVariables.Remove(key);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5443", Justification = "Private temp directory created with 0700 permissions")]
#pragma warning disable S5443
    public static string GetSafeTempDirectory()
    {
        var privateDir = Path.Combine(Path.GetTempPath(), "leecharr-" + Environment.ProcessId);
        UnixPrivatePermissions.EnsurePrivateTempDirectory(privateDir);
        return privateDir;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5443", Justification = "Private temp directory created with 0700 permissions")]
    public static Dictionary<string, string> BuildSafeEnvironment(string workingDirectory)
    {
        return BuildSafeEnvironment(ReadProcessEnvironment(), workingDirectory);
    }

    public static Dictionary<string, string> BuildSafeEnvironment(
        IReadOnlyDictionary<string, string> sourceEnvironment,
        string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(sourceEnvironment);

        var path = GetSourceValue(sourceEnvironment, "PATH") ?? "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        var home = GetSourceValue(sourceEnvironment, "HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = GetSourceValue(sourceEnvironment, "USER")
            ?? GetSourceValue(sourceEnvironment, "USERNAME")
            ?? "leecharr";
        var shell = GetSourceValue(sourceEnvironment, "SHELL")
            ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh"));
        var tmpdir = GetSourceValue(sourceEnvironment, "TMPDIR");
        if (string.IsNullOrWhiteSpace(tmpdir))
        {
            tmpdir = GetSafeTempDirectory();
        }

        var lang = GetSourceValue(sourceEnvironment, "LANG") ?? "en_US.UTF-8";
        var pwd = !string.IsNullOrWhiteSpace(workingDirectory)
            ? workingDirectory
            : (GetSourceValue(sourceEnvironment, "PWD") ?? Directory.GetCurrentDirectory());

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SetIfPresent(environment, "PATH", path);
        SetIfPresent(environment, "HOME", home);
        SetIfPresent(environment, "USER", user);
        SetIfPresent(environment, "SHELL", shell);
        SetIfPresent(environment, "TMPDIR", tmpdir);
        SetIfPresent(environment, "PWD", pwd);
        environment["TERM"] = "xterm-256color";
        environment["COLORTERM"] = "truecolor";
        environment["LANG"] = lang;
        environment["PS1"] = @"\[\e[1;32m\]\u@leecharr\[\e[0m\]:\[\e[1;34m\]\w\[\e[0m\]\$ ";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            SetIfPresent(environment, "SystemRoot", GetSourceValue(sourceEnvironment, "SystemRoot"));
            SetIfPresent(environment, "windir", GetSourceValue(sourceEnvironment, "windir"));
            SetIfPresent(environment, "PATHEXT", GetSourceValue(sourceEnvironment, "PATHEXT"));
            SetIfPresent(environment, "USERPROFILE", GetSourceValue(sourceEnvironment, "USERPROFILE"));
        }

        return environment;
    }

    public static void Sanitize(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? null
            : startInfo.WorkingDirectory;

        startInfo.Environment.Clear();
        foreach (var pair in BuildSafeEnvironment(workingDirectory))
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        StripSensitiveKeys(startInfo);
    }

    private static Dictionary<string, string> ReadProcessEnvironment()
    {
        var source = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            source[key] = entry.Value?.ToString() ?? string.Empty;
        }

        return source;
    }

    private static string GetSourceValue(IReadOnlyDictionary<string, string> sourceEnvironment, string key)
    {
        if (sourceEnvironment.TryGetValue(key, out var value))
        {
            return value;
        }

        foreach (var pair in sourceEnvironment)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static void SetIfPresent(Dictionary<string, string> environment, string key, string value)
    {
        if (!string.IsNullOrEmpty(value) && !IsSensitiveKey(key))
        {
            environment[key] = value;
        }
    }
#pragma warning restore S5443
}
