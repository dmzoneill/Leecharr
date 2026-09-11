// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Leecharr.Http.Terminal;

public sealed class LinuxPtySession : ITerminalSession
{
    private readonly int masterFd;
    private readonly int pid;
    private int disposed;

    public int ProcessId => this.pid;

    public bool IsActive => this.disposed == 0;

    private LinuxPtySession(int masterFd, int pid)
    {
        this.masterFd = masterFd;
        this.pid = pid;
        this.StartWatcher();
    }

    public static LinuxPtySession Start(string cwd, int cols, int rows)
    {
        var ws = new NativePty.Winsize
        {
            WsCol = (ushort)Math.Max(10, Math.Min(cols, 500)),
            WsRow = (ushort)Math.Max(5, Math.Min(rows, 200)),
        };

        string safeCwd = !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd) ? Path.GetFullPath(cwd) : null;
        string shell = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        string[] argv = [shell, "-i"];

        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            var val = entry.Value?.ToString();
            if (!string.IsNullOrEmpty(key) && !TerminalEnvironmentSanitizer.IsSensitiveKey(key))
            {
                envVars[key] = val ?? string.Empty;
            }
        }

        envVars["TERM"] = "xterm-256color";
        envVars["COLORTERM"] = "truecolor";
        if (!envVars.ContainsKey("LANG"))
        {
            envVars["LANG"] = "en_US.UTF-8";
        }

        if (!envVars.ContainsKey("PATH"))
        {
            envVars["PATH"] = "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin";
        }

        if (!string.IsNullOrEmpty(safeCwd))
        {
            envVars["PWD"] = safeCwd;
        }

        var envStrings = envVars.Select(kv => $"{kv.Key}={kv.Value}").ToArray();

        IntPtr cwdPtr = safeCwd != null ? Marshal.StringToCoTaskMemUTF8(safeCwd) : IntPtr.Zero;
        IntPtr shellPtr = Marshal.StringToCoTaskMemUTF8(shell);
        IntPtr argvArrayPtr = AllocateNativeStringArray(argv, out var argvPointers);
        IntPtr envArrayPtr = AllocateNativeStringArray(envStrings, out var envPointers);

        int masterFd = -1;
        int pid = -1;

        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.Chdir)).MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.ExecveRaw)).MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.ExecvpRaw)).MethodHandle);
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(typeof(NativePty).GetMethod(nameof(NativePty.Exit)).MethodHandle);

        try
        {
            pid = NativePty.Forkpty(out masterFd, IntPtr.Zero, IntPtr.Zero, ref ws);
            if (pid < 0)
            {
                throw new InvalidOperationException($"Failed to fork pseudo-terminal (errno: {Marshal.GetLastWin32Error()})");
            }

            if (pid == 0)
            {
                // Child process: Only invoke async-signal-safe functions (chdir, execve, _exit).
                // Zero managed memory allocations, zero runtime locks, zero setenv/malloc calls.
                if (cwdPtr != IntPtr.Zero)
                {
                    NativePty.Chdir(cwdPtr);
                }

                NativePty.ExecveRaw(shellPtr, argvArrayPtr, envArrayPtr);

                // Fallback if execve fails
                NativePty.ExecvpRaw(shellPtr, argvArrayPtr);

                NativePty.Exit(1);
            }
        }
        finally
        {
            if (cwdPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(cwdPtr);
            }

            if (shellPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(shellPtr);
            }

            FreeNativeStringArray(argvArrayPtr, argvPointers);
            FreeNativeStringArray(envArrayPtr, envPointers);
        }

        return new LinuxPtySession(masterFd, pid);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this.disposed != 0)
        {
            return 0;
        }

        var temp = new byte[buffer.Length];
        return await Task.Run(
            () =>
            {
                nint bytesRead = NativePty.Read(this.masterFd, temp, (nuint)temp.Length);
                if (bytesRead <= 0)
                {
                    return 0;
                }

                temp.AsSpan(0, (int)bytesRead).CopyTo(buffer.Span);
                return (int)bytesRead;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this.disposed != 0 || buffer.IsEmpty)
        {
            return;
        }

        var temp = buffer.ToArray();
        await Task.Run(
            () =>
            {
                NativePty.Write(this.masterFd, temp, (nuint)temp.Length);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public void Resize(int cols, int rows)
    {
        if (this.disposed != 0)
        {
            return;
        }

        var ws = new NativePty.Winsize
        {
            WsCol = (ushort)Math.Max(10, Math.Min(cols, 500)),
            WsRow = (ushort)Math.Max(5, Math.Min(rows, 200)),
        };

        NativePty.Ioctl(this.masterFd, NativePty.TIOCSWINSZ, ref ws);
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        try
        {
            NativePty.Close(this.masterFd);
        }
        catch
        {
            // Ignored on teardown
        }

        if (this.pid <= 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                NativePty.Kill(this.pid, 15); // SIGTERM
            }
            catch
            {
            }

            var sw = Stopwatch.StartNew();
            bool reaped = false;

            while (sw.ElapsedMilliseconds < 1500)
            {
                int res = NativePty.Waitpid(this.pid, out _, 1); // WNOHANG
                if (res > 0 || res < 0)
                {
                    reaped = true;
                    break;
                }

                await Task.Delay(50).ConfigureAwait(false);
            }

            if (!reaped)
            {
                try
                {
                    NativePty.Kill(this.pid, 9); // SIGKILL
                }
                catch
                {
                }

                var killSw = Stopwatch.StartNew();
                while (killSw.ElapsedMilliseconds < 2000)
                {
                    int res = NativePty.Waitpid(this.pid, out _, 1); // WNOHANG
                    if (res > 0 || res < 0)
                    {
                        break;
                    }

                    await Task.Delay(50).ConfigureAwait(false);
                }
            }
        });
    }

    public ValueTask DisposeAsync()
    {
        this.Kill();
        return ValueTask.CompletedTask;
    }

    private static IntPtr AllocateNativeStringArray(string[] array, out IntPtr[] elementPointers)
    {
        elementPointers = new IntPtr[array.Length + 1];
        for (int i = 0; i < array.Length; i++)
        {
            elementPointers[i] = Marshal.StringToCoTaskMemUTF8(array[i]);
        }

        elementPointers[^1] = IntPtr.Zero;

        IntPtr arrayPtr = Marshal.AllocHGlobal(IntPtr.Size * elementPointers.Length);
        Marshal.Copy(elementPointers, 0, arrayPtr, elementPointers.Length);
        return arrayPtr;
    }

    private static void FreeNativeStringArray(IntPtr arrayPtr, IntPtr[] elementPointers)
    {
        if (elementPointers != null)
        {
            foreach (var ptr in elementPointers)
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }

        if (arrayPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(arrayPtr);
        }
    }

    private void StartWatcher()
    {
        if (this.pid <= 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            while (this.disposed == 0)
            {
                int res = NativePty.Waitpid(this.pid, out _, 1); // WNOHANG
                if (res > 0 || res < 0)
                {
                    this.Kill();
                    return;
                }

                await Task.Delay(200).ConfigureAwait(false);
            }
        });
    }
}
