// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Leecharr.Http.Terminal;

public sealed class FallbackProcessSession : ITerminalSession
{
    private readonly Process process;
    private readonly Stream inputStream;
    private readonly Stream outputStream;
    private int disposed;

    public int ProcessId => this.process.Id;

    public bool IsActive => this.disposed == 0 && !this.process.HasExited;

    private FallbackProcessSession(Process process)
    {
        this.process = process;
        this.inputStream = process.StandardInput.BaseStream;
        this.outputStream = process.StandardOutput.BaseStream;
    }

    public static FallbackProcessSession Start(string cwd, int cols, int rows)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "powershell.exe" : "/bin/sh";
        var args = isWindows ? "-NoLogo" : "-i";

        var startInfo = new ProcessStartInfo
        {
            FileName = shell,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd))
        {
            startInfo.WorkingDirectory = cwd;
        }

        startInfo.EnvironmentVariables["TERM"] = "xterm-256color";

        var proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch fallback terminal process");

        return new FallbackProcessSession(proc);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this.disposed != 0 || this.process.HasExited)
        {
            return 0;
        }

        try
        {
            return await this.outputStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return 0;
        }
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        if (this.disposed != 0 || this.process.HasExited)
        {
            return;
        }

        try
        {
            await this.inputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            await this.inputStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Shell pipe broken
        }
    }

    public void Resize(int cols, int rows)
    {
        // Standard process streams do not support TIOCSWINSZ
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (!this.process.HasExited)
            {
                this.process.Kill(entireProcessTree: true);
            }

            this.process.Dispose();
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
