// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

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

    public static void Sanitize(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = Environment.GetEnvironmentVariable("USER") ?? Environment.GetEnvironmentVariable("USERNAME") ?? "leecharr";
        var shell = Environment.GetEnvironmentVariable("SHELL") ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh"));
        var tmpdir = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath();
        var lang = Environment.GetEnvironmentVariable("LANG") ?? "en_US.UTF-8";
        var pwd = !string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? startInfo.WorkingDirectory
            : (Environment.GetEnvironmentVariable("PWD") ?? Directory.GetCurrentDirectory());

        startInfo.Environment.Clear();

        if (!string.IsNullOrEmpty(path))
        {
            startInfo.Environment["PATH"] = path;
        }

        if (!string.IsNullOrEmpty(home))
        {
            startInfo.Environment["HOME"] = home;
        }

        if (!string.IsNullOrEmpty(user))
        {
            startInfo.Environment["USER"] = user;
        }

        if (!string.IsNullOrEmpty(shell))
        {
            startInfo.Environment["SHELL"] = shell;
        }

        if (!string.IsNullOrEmpty(tmpdir))
        {
            startInfo.Environment["TMPDIR"] = tmpdir;
        }

        if (!string.IsNullOrEmpty(pwd))
        {
            startInfo.Environment["PWD"] = pwd;
        }

        startInfo.Environment["TERM"] = "xterm-256color";
        startInfo.Environment["COLORTERM"] = "truecolor";
        startInfo.Environment["LANG"] = lang;
        startInfo.Environment["PS1"] = @"\[\e[1;32m\]\u@leecharr\[\e[0m\]:\[\e[1;34m\]\w\[\e[0m\]\$ ";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (!string.IsNullOrEmpty(systemRoot))
            {
                startInfo.Environment["SystemRoot"] = systemRoot;
            }

            var winDir = Environment.GetEnvironmentVariable("windir");
            if (!string.IsNullOrEmpty(winDir))
            {
                startInfo.Environment["windir"] = winDir;
            }

            var pathext = Environment.GetEnvironmentVariable("PATHEXT");
            if (!string.IsNullOrEmpty(pathext))
            {
                startInfo.Environment["PATHEXT"] = pathext;
            }

            var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(userProfile))
            {
                startInfo.Environment["USERPROFILE"] = userProfile;
            }
        }

        StripSensitiveKeys(startInfo);
    }
}
