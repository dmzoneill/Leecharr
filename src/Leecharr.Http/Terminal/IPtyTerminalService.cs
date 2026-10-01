// Copyright (c) FeedItOut. All rights reserved.

namespace Leecharr.Http.Terminal;

public interface IPtyTerminalService
{
    ITerminalSession CreateSession(string cwd, int cols, int rows);
}
