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
    private const string PythonPtyScript = @"import os, pty, struct, fcntl, termios, sys, select, signal

cwd = sys.argv[1] if len(sys.argv) > 1 else '/tmp'
cols = int(sys.argv[2]) if len(sys.argv) > 2 else 80
rows = int(sys.argv[3]) if len(sys.argv) > 3 else 24
ctrl_pipe = sys.argv[4] if len(sys.argv) > 4 else None

master, slave = pty.openpty()
winsize = struct.pack('HHHH', rows, cols, 0, 0)
try:
    fcntl.ioctl(master, termios.TIOCSWINSZ, winsize)
except:
    pass

ctrl_fd = None
if ctrl_pipe and os.path.exists(ctrl_pipe):
    try:
        ctrl_fd = os.open(ctrl_pipe, os.O_RDWR | os.O_NONBLOCK)
    except:
        ctrl_fd = None

pid = os.fork()
if pid == 0:
    if ctrl_fd is not None:
        try:
            os.close(ctrl_fd)
        except:
            pass
    try:
        os.close(master)
    except:
        pass
    os.setsid()
    try:
        fcntl.ioctl(slave, termios.TIOCSCTTY, 0)
    except:
        pass
    os.dup2(slave, 0)
    os.dup2(slave, 1)
    os.dup2(slave, 2)
    try:
        max_fd = os.sysconf('SC_OPEN_MAX')
    except:
        max_fd = 1024
    try:
        os.closerange(3, max_fd)
    except:
        pass
    try:
        os.chdir(cwd)
    except:
        pass
    os.environ['TERM'] = 'xterm-256color'
    os.environ['COLORTERM'] = 'truecolor'
    if 'LANG' not in os.environ:
        os.environ['LANG'] = 'en_US.UTF-8'
    shell_bin = '/bin/bash' if os.path.exists('/bin/bash') else '/bin/sh'
    shell_name = os.path.basename(shell_bin)
    try:
        os.execlp(shell_bin, shell_name, '-i')
    except:
        os.execlp('/bin/sh', 'sh', '-i')
else:
    try:
        os.close(slave)
    except:
        pass
    rfds = [0, master]
    if ctrl_fd is not None:
        rfds.append(ctrl_fd)
    while True:
        try:
            r, _, _ = select.select(rfds, [], [])
        except (InterruptedError, select.error):
            continue
        if 0 in r:
            try:
                data = os.read(0, 4096)
                if not data:
                    break
                os.write(master, data)
            except OSError:
                break
        if master in r:
            try:
                data = os.read(master, 4096)
                if not data:
                    break
                os.write(1, data)
            except OSError:
                break
        if ctrl_fd is not None and ctrl_fd in r:
            try:
                ctrl_data = os.read(ctrl_fd, 512).decode('utf-8', errors='ignore')
                if ctrl_data:
                    for line in ctrl_data.strip().split('\n'):
                        line = line.strip()
                        if ':' in line:
                            parts = line.split(':')
                            r_rows, r_cols = int(parts[0]), int(parts[1])
                            winsize = struct.pack('HHHH', r_rows, r_cols, 0, 0)
                            try:
                                fcntl.ioctl(master, termios.TIOCSWINSZ, winsize)
                            except:
                                pass
                            try:
                                os.kill(pid, signal.SIGWINCH)
                            except:
                                pass
            except:
                pass
";

    private readonly Process process;
    private readonly Stream inputStream;
    private readonly Stream outputStream;
    private readonly string controlPipePath;
    private FileStream controlPipeStream;
    private int disposed;

    public int ProcessId => this.process.Id;

    public bool IsActive => this.disposed == 0 && !this.process.HasExited;

    private PtyProcessSession(Process process, string controlPipePath = null, FileStream controlPipeStream = null)
    {
        this.process = process;
        this.inputStream = process.StandardInput.BaseStream;
        this.outputStream = process.StandardOutput.BaseStream;
        this.controlPipePath = controlPipePath;
        this.controlPipeStream = controlPipeStream;
    }

    public static PtyProcessSession Start(string cwd, int cols, int rows)
    {
        var safeCwd = !string.IsNullOrWhiteSpace(cwd) && Directory.Exists(cwd) ? cwd : "/tmp";
        string controlPipePath = null;
        FileStream controlPipeStream = null;

        ProcessStartInfo startInfo;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && (File.Exists("/usr/bin/python3") || File.Exists("/bin/python3")))
        {
            var pyBinary = File.Exists("/usr/bin/python3") ? "/usr/bin/python3" : "/bin/python3";
            controlPipePath = Path.Combine(Path.GetTempPath(), $"leecharr_pty_ctrl_{Guid.NewGuid():N}.pipe");
            CreateFifo(controlPipePath);

            var b64Script = Convert.ToBase64String(Encoding.UTF8.GetBytes(PythonPtyScript));
            var pyCommand = $"import base64; exec(base64.b64decode('{b64Script}'))";

            startInfo = new ProcessStartInfo
            {
                FileName = pyBinary,
                Arguments = $"-u -c \"{pyCommand}\" \"{safeCwd}\" {cols} {rows} \"{controlPipePath}\"",
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

        if (controlPipePath != null && File.Exists(controlPipePath))
        {
            try
            {
                controlPipeStream = new FileStream(controlPipePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
            catch
            {
                controlPipeStream = null;
            }
        }

        return new PtyProcessSession(proc, controlPipePath, controlPipeStream);
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
        if (this.disposed != 0 || this.process.HasExited)
        {
            return;
        }

        if (this.controlPipeStream != null)
        {
            try
            {
                var msg = Encoding.UTF8.GetBytes($"{rows}:{cols}\n");
                this.controlPipeStream.Write(msg, 0, msg.Length);
                this.controlPipeStream.Flush();
            }
            catch
            {
                // Shell or control channel closed
            }
        }
    }

    public void Kill()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (this.controlPipeStream != null)
            {
                try
                {
                    this.controlPipeStream.Dispose();
                }
                catch
                {
                }

                this.controlPipeStream = null;
            }

            if (this.controlPipePath != null && File.Exists(this.controlPipePath))
            {
                try
                {
                    File.Delete(this.controlPipePath);
                }
                catch
                {
                }
            }

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

    private static void CreateFifo(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                if (MkFifo(path, 438 /* 0666 */) == 0)
                {
                    return;
                }
            }
        }
        catch
        {
            // Fallback to mkfifo process
        }

        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "mkfifo",
                Arguments = $"\"{path}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            proc?.WaitForExit(1000);
        }
        catch
        {
            // Ignored
        }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);
}
