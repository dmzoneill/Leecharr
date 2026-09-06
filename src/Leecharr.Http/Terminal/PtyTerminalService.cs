// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Security;
using NzbDrone.Core.Configuration;

namespace Leecharr.Http.Terminal;

public class PtyTerminalService : IPtyTerminalService
{
    private readonly IConfigFileProvider configFileProvider;

    public PtyTerminalService(IConfigFileProvider configFileProvider = null)
    {
        this.configFileProvider = configFileProvider;
    }

    public ITerminalSession CreateSession(string cwd, int cols, int rows)
    {
        // 1. Validate security configuration / permissions
        if (this.configFileProvider != null && !this.IsTerminalAccessPermitted())
        {
            throw new SecurityException("Terminal process execution is prohibited by security configuration.");
        }

        // 2. Validate and sanitize working directory
        string sanitizedCwd = null;
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            if (cwd.Contains('\0') || cwd.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException("Invalid working directory path specified.", nameof(cwd));
            }

            try
            {
                var fullPath = Path.GetFullPath(cwd);
                if (Directory.Exists(fullPath))
                {
                    sanitizedCwd = fullPath;
                }
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Failed to resolve working directory: {ex.Message}", nameof(cwd), ex);
            }
        }

        if (sanitizedCwd == null)
        {
            sanitizedCwd = Directory.GetCurrentDirectory();
        }

        // 3. Clamp dimensions to safe bounds
        int clampedCols = Math.Clamp(cols, 10, 500);
        int clampedRows = Math.Clamp(rows, 5, 200);

        if (File.Exists("/usr/bin/python3") || File.Exists("/bin/python3"))
        {
            return PtyProcessSession.Start(sanitizedCwd, clampedCols, clampedRows);
        }

        return FallbackProcessSession.Start(sanitizedCwd, clampedCols, clampedRows);
    }

    private bool IsTerminalAccessPermitted()
    {
        return this.configFileProvider?.TerminalAccessEnabled == true;
    }
}
