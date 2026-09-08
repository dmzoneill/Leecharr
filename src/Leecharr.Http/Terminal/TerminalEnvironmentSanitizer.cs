// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Leecharr.Http.Terminal;

public static class TerminalEnvironmentSanitizer
{
    public static void Sanitize(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        var home = Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var user = Environment.GetEnvironmentVariable("USER") ?? Environment.GetEnvironmentVariable("USERNAME") ?? "leecharr";
        var shell = Environment.GetEnvironmentVariable("SHELL") ?? (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh"));
        var tmpdir = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath();
        var lang = Environment.GetEnvironmentVariable("LANG") ?? "en_US.UTF-8";

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
    }
}
