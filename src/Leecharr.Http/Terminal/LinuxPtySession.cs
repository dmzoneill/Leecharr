// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
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
    }

    public static LinuxPtySession Start(string cwd, int cols, int rows)
    {
        var ws = new NativePty.Winsize
        {
            WsCol = (ushort)Math.Max(10, Math.Min(cols, 500)),
            WsRow = (ushort)Math.Max(5, Math.Min(rows, 200)),
        };

        int pid = NativePty.Forkpty(out int masterFd, IntPtr.Zero, IntPtr.Zero, ref ws);
        if (pid < 0)
        {
            throw new InvalidOperationException($"Failed to fork pseudo-terminal (errno: {Marshal.GetLastWin32Error()})");
        }

        if (pid == 0)
        {
            // Child process: set directory, environment and launch shell
            try
            {
                if (!string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd))
                {
                    NativePty.Chdir(cwd);
                }

                NativePty.Setenv("TERM", "xterm-256color", 1);
                NativePty.Setenv("COLORTERM", "truecolor", 1);
                NativePty.Setenv("LANG", "en_US.UTF-8", 0);

                if (File.Exists("/bin/bash"))
                {
                    NativePty.ExecCommand("/bin/bash", new[] { "-i" });
                }

                NativePty.ExecCommand("/bin/sh", new[] { "-i" });
            }
            catch
            {
                // Fallthrough to exit
            }

            NativePty.Exit(1);
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
            NativePty.Kill(this.pid, 15); // SIGTERM
            NativePty.Close(this.masterFd);
            NativePty.Waitpid(this.pid, out _, 1); // WNOHANG
        }
        catch
        {
            // Ignored on teardown
        }
    }

    public ValueTask DisposeAsync()
    {
        this.Kill();
        return ValueTask.CompletedTask;
    }
}
