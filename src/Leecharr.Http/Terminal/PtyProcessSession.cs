// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Leecharr.Http.Terminal;

public sealed class PtyProcessSession : ITerminalSession
{
    private const string PythonPtyScript = @"import os, pty, struct, fcntl, termios, sys, select

cwd = sys.argv[1] if len(sys.argv) > 1 else '/tmp'
cols = int(sys.argv[2]) if len(sys.argv) > 2 else 80
rows = int(sys.argv[3]) if len(sys.argv) > 3 else 24

master, slave = pty.openpty()
winsize = struct.pack('HHHH', rows, cols, 0, 0)
fcntl.ioctl(master, termios.TIOCSWINSZ, winsize)

pid = os.fork()
if pid == 0:
    os.close(master)
    os.setsid()
    os.dup2(slave, 0)
    os.dup2(slave, 1)
    os.dup2(slave, 2)
    if slave > 2:
        os.close(slave)
    try:
        os.chdir(cwd)
    except:
        pass
    os.environ['TERM'] = 'xterm-256color'
    os.environ['COLORTERM'] = 'truecolor'
    try:
        os.execlp('/bin/bash', '/bin/bash', '-i')
    except:
        os.execlp('/bin/sh', '/bin/sh', '-i')
else:
    os.close(slave)
    while True:
        r, _, _ = select.select([0, master], [], [])
        if 0 in r:
            data = os.read(0, 4096)
            if not data:
                break
            os.write(master, data)
        if master in r:
            try:
                data = os.read(master, 4096)
                if not data:
                    break
                os.write(1, data)
            except OSError:
                break
";

    private readonly Process process;
    private readonly Stream inputStream;
    private readonly Stream outputStream;
    private int disposed;

    public int ProcessId => this.process.Id;

    public bool IsActive => this.disposed == 0 && !this.process.HasExited;

    private PtyProcessSession(Process process)
    {
        this.process = process;
        this.inputStream = process.StandardInput.BaseStream;
        this.outputStream = process.StandardOutput.BaseStream;
    }

    public static PtyProcessSession Start(string cwd, int cols, int rows)
    {
        var safeCwd = !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd) ? cwd : "/tmp";

        ProcessStartInfo startInfo;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (File.Exists("/usr/bin/python3") || File.Exists("/bin/python3")))
        {
            var pyBinary = File.Exists("/usr/bin/python3") ? "/usr/bin/python3" : "/bin/python3";
            var b64Script = Convert.ToBase64String(Encoding.UTF8.GetBytes(PythonPtyScript));
            var pyCommand = $"import base64; exec(base64.b64decode('{b64Script}'))";

            startInfo = new ProcessStartInfo
            {
                FileName = pyBinary,
                Arguments = $"-u -c \"{pyCommand}\" \"{safeCwd}\" {cols} {rows}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = safeCwd,
            };
        }
        else
        {
            var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            var shell = isWindows ? "powershell.exe" : (File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh");
            var args = isWindows ? "-NoLogo" : "-i";

            startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = args,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = safeCwd,
            };
        }

        startInfo.EnvironmentVariables["TERM"] = "xterm-256color";
        startInfo.EnvironmentVariables["COLORTERM"] = "truecolor";

        var proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch terminal process");

        return new PtyProcessSession(proc);
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
            // Shell closed
        }
    }

    public void Resize(int cols, int rows)
    {
        // Handled dynamically
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
