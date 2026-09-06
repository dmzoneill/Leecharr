// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Leecharr.Http.Terminal;

public sealed class FallbackProcessSession : ITerminalSession
{
    private readonly Process process;
    private readonly Stream inputStream;
    private readonly Channel<byte[]> outputChannel;
    private readonly CancellationTokenSource sessionCts = new();
    private byte[] pendingChunk;
    private int pendingOffset;
    private int disposed;

    public int ProcessId => this.process.Id;

    public bool IsActive => this.disposed == 0 && !this.process.HasExited;

    private FallbackProcessSession(Process process)
    {
        this.process = process;
        this.inputStream = process.StandardInput.BaseStream;
        this.outputChannel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true,
        });

        _ = this.PumpStreamAsync(process.StandardOutput.BaseStream, this.sessionCts.Token);
        _ = this.PumpStreamAsync(process.StandardError.BaseStream, this.sessionCts.Token);
    }

    public static FallbackProcessSession Start(string cwd, int cols, int rows)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var shell = isWindows ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh");
        var args = isWindows ? "-NoLogo" : (File.Exists("/bin/bash") ? "--noprofile --norc -i" : "-i");

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
        startInfo.EnvironmentVariables["COLORTERM"] = "truecolor";
        startInfo.EnvironmentVariables["LANG"] = "en_US.UTF-8";
        startInfo.EnvironmentVariables["PS1"] = @"\[\e[1;32m\]\u@leecharr\[\e[0m\]:\[\e[1;34m\]\w\[\e[0m\]\$ ";

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
            while (this.pendingChunk == null || this.pendingOffset >= this.pendingChunk.Length)
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, this.sessionCts.Token);
                if (!await this.outputChannel.Reader.WaitToReadAsync(linkedCts.Token).ConfigureAwait(false))
                {
                    return 0;
                }

                if (this.outputChannel.Reader.TryRead(out var chunk))
                {
                    this.pendingChunk = chunk;
                    this.pendingOffset = 0;
                }
            }

            int toCopy = Math.Min(buffer.Length, this.pendingChunk.Length - this.pendingOffset);
            this.pendingChunk.AsMemory(this.pendingOffset, toCopy).CopyTo(buffer);
            this.pendingOffset += toCopy;
            return toCopy;
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
            this.sessionCts.Cancel();
            this.sessionCts.Dispose();
            this.outputChannel.Writer.TryComplete();

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

    private async Task PumpStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (bytesRead <= 0)
                {
                    break;
                }

                var chunk = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, chunk, 0, bytesRead);
                await this.outputChannel.Writer.WriteAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Stream closed or cancelled
        }
    }
}
