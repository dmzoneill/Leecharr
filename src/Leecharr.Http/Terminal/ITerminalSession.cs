// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Leecharr.Http.Terminal;

public interface ITerminalSession : IAsyncDisposable
{
    int ProcessId { get; }

    bool IsActive { get; }

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);

    void Resize(int cols, int rows);

    void Kill();
}
