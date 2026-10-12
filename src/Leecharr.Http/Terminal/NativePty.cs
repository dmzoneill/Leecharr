// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Runtime.InteropServices;

namespace Leecharr.Http.Terminal;

public static class NativePty
{
    public const ulong TIOCSWINSZ = 0x5414;

    [StructLayout(LayoutKind.Sequential)]
    public struct Winsize
    {
        public ushort WsRow;
        public ushort WsCol;
        public ushort WsXpixel;
        public ushort WsYpixel;
    }

    [DllImport("libc", EntryPoint = "forkpty", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Forkpty(out int amaster, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Ioctl(int fd, ulong request, ref Winsize winp);

    [DllImport("libc", EntryPoint = "chdir", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Chdir(IntPtr path);

    [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int ExecveRaw(IntPtr file, IntPtr argv, IntPtr envp);

    [DllImport("libc", EntryPoint = "execvp", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int ExecvpRaw(IntPtr file, IntPtr argv);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern nint Read(int fd, [Out] byte[] buf, nuint count);

    private const int ErrnoEintr = 4;

    public static int KillProcessGroup(int leaderPid, int sig)
    {
        if (leaderPid <= 0)
        {
            return -1;
        }

        return Kill(-leaderPid, sig);
    }

    /// <summary>
    /// Reads from a PTY master fd, retrying when interrupted by a signal.
    /// Returns bytes read, 0 for EOF, or a negative value for a non-EINTR read error.
    /// </summary>
    public static nint ReadRespectingEintr(int fd, byte[] buf)
    {
        while (true)
        {
            var bytesRead = Read(fd, buf, (nuint)buf.Length);
            if (bytesRead > 0 || bytesRead == 0)
            {
                return bytesRead;
            }

            if (Marshal.GetLastWin32Error() != ErrnoEintr)
            {
                return bytesRead;
            }
        }
    }

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    private static extern nint WriteRaw(int fd, IntPtr buf, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern nint Write(int fd, [In] byte[] buf, nuint count);

    public static void WriteFully(int fd, byte[] buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var offset = 0;
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var basePtr = handle.AddrOfPinnedObject();
            while (offset < buffer.Length)
            {
                var remaining = (nuint)(buffer.Length - offset);
                var written = WriteRaw(fd, basePtr + offset, remaining);
                if (written < 0)
                {
                    if (Marshal.GetLastWin32Error() == ErrnoEintr)
                    {
                        continue;
                    }

                    return;
                }

                if (written == 0)
                {
                    return;
                }

                offset += (int)written;
            }
        }
        finally
        {
            handle.Free();
        }
    }

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Kill(int pid, int sig);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern int Waitpid(int pid, out int status, int options);

    [DllImport("libc", EntryPoint = "_exit", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
    public static extern void Exit(int status);
}
