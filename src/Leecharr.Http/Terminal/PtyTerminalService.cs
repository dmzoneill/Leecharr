// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Runtime.InteropServices;

namespace Leecharr.Http.Terminal;

public class PtyTerminalService : IPtyTerminalService
{
    public ITerminalSession CreateSession(string cwd, int cols, int rows)
    {
        return FallbackProcessSession.Start(cwd, cols, rows);
    }
}
