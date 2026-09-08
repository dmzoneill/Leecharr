// Copyright (c) PlaceholderCompany. All rights reserved.

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
    public static extern int Forkpty(out int amaster, IntPtr name, IntPtr termp, ref Winsize winp);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    public static extern int Ioctl(int fd, ulong request, ref Winsize winp);

    [DllImport("libc", EntryPoint = "chdir", SetLastError = true)]
    public static extern int Chdir(IntPtr path);

    [DllImport("libc", EntryPoint = "execve", SetLastError = true)]
    public static extern int ExecveRaw(IntPtr file, IntPtr argv, IntPtr envp);

    [DllImport("libc", EntryPoint = "execvp", SetLastError = true)]
    public static extern int ExecvpRaw(IntPtr file, IntPtr argv);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    public static extern nint Read(int fd, [Out] byte[] buf, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    public static extern nint Write(int fd, [In] byte[] buf, nuint count);

    [DllImport("libc", EntryPoint = "close", SetLastError = true)]
    public static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    public static extern int Kill(int pid, int sig);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    public static extern int Waitpid(int pid, out int status, int options);

    [DllImport("libc", EntryPoint = "_exit", SetLastError = true)]
    public static extern void Exit(int status);
}
