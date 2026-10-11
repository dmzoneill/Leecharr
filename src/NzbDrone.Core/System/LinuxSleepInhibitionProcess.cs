// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NzbDrone.Core.SystemServices;

internal static class LinuxSleepInhibitionProcess
{
    private const int PrSetPdeathsig = 1;
    private const int SigTerm = 15;

    [DllImport("libc", EntryPoint = "fork", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Fork();

    [DllImport("libc", EntryPoint = "prctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Prctl(int option, ulong arg2, ulong arg3, ulong arg4, ulong arg5);

    [DllImport("libc", EntryPoint = "execvp", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern int Execvp(string file, string[] argv);

    [DllImport("libc", EntryPoint = "_exit", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern void Exit(int status);

    internal static Process Start(string executable, string[] arguments)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException("Executable path is required", nameof(executable));
        }

        arguments ??= Array.Empty<string>();

        var argv = new string[arguments.Length + 2];
        argv[0] = executable;
        Array.Copy(arguments, 0, argv, 1, arguments.Length);
        argv[^1] = null;

        var pid = Fork();
        if (pid < 0)
        {
            throw new InvalidOperationException("fork() failed while starting Linux sleep inhibitor");
        }

        if (pid == 0)
        {
            if (Prctl(PrSetPdeathsig, SigTerm, 0, 0, 0) != 0)
            {
                Exit(1);
            }

            if (Execvp(executable, argv) != 0)
            {
                Exit(127);
            }

            Exit(0);
        }

        try
        {
            return Process.GetProcessById(pid);
        }
        catch
        {
            try
            {
                Process.GetProcessById(pid).Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort cleanup if the child exited before we could attach.
            }

            throw;
        }
    }
}
