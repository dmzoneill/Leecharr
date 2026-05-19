using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.BitTorrent;

public class BlockRequest
{
    public int PieceIndex { get; set; }
    public int BlockOffset { get; set; }
    public int BlockLength { get; set; }
    public DateTime RequestedAt { get; set; }
}

public class PieceState
{
    public int Index { get; set; }
    public int Length { get; set; }
    public int TotalBlocks { get; set; }
    public int ReceivedBlocks { get; set; }
    public bool IsComplete { get; set; }
    public bool IsVerified { get; set; }
    public bool[] BlockBitfield { get; set; }
    public int Priority { get; set; } = 1; // 0 = skip, 1 = normal, 2 = high, 3 = max
}

public class PiecePicker
{
    public const int DefaultBlockSize = 16384; // 16 KB standard BitTorrent block size

    private readonly object _syncLock = new();
    private readonly int _pieceCount;
    private readonly int _pieceLength;
    private readonly long _totalSize;
    private readonly PieceState[] _pieces;
    private readonly int[] _swarmAvailability;
    private readonly HashSet<string> _inFlightBlocks = new();

    public PiecePicker(int pieceCount, int pieceLength, long totalSize)
    {
        _pieceCount = pieceCount;
        _pieceLength = pieceLength;
        _totalSize = totalSize;
        _pieces = new PieceState[pieceCount];
        _swarmAvailability = new int[pieceCount];

        for (var i = 0; i < pieceCount; i++)
        {
            var len = (i == pieceCount - 1) ? (int)(totalSize - ((long)i * pieceLength)) : pieceLength;
            var totalBlocks = (int)Math.Ceiling((double)len / DefaultBlockSize);

            _pieces[i] = new PieceState
            {
                Index = i,
                Length = len,
                TotalBlocks = totalBlocks,
                BlockBitfield = new bool[totalBlocks]
            };
        }
    }

    public int PieceCount => _pieceCount;
    public int PieceLength => _pieceLength;
    public long TotalSize => _totalSize;

    public void UpdatePeerAvailability(bool[] peerBitfield, bool isAdd)
    {
        if (peerBitfield == null)
        {
            return;
        }

        lock (_syncLock)
        {
            var limit = Math.Min(_pieceCount, peerBitfield.Length);
            for (var i = 0; i < limit; i++)
            {
                if (peerBitfield[i])
                {
                    _swarmAvailability[i] = Math.Max(0, _swarmAvailability[i] + (isAdd ? 1 : -1));
                }
            }
        }
    }

    public void SetPiecePriority(int pieceIndex, int priority)
    {
        lock (_syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < _pieceCount)
            {
                _pieces[pieceIndex].Priority = priority;
            }
        }
    }

    public bool IsEndgameMode()
    {
        lock (_syncLock)
        {
            var remainingBlocks = _pieces.Where(p => p.Priority > 0 && !p.IsComplete)
                .Sum(p => p.TotalBlocks - p.ReceivedBlocks);

            return remainingBlocks > 0 && remainingBlocks <= 30;
        }
    }

    public List<BlockRequest> PickBlocks(bool[] peerBitfield, int maxRequests, bool sequentialMode = false)
    {
        var requests = new List<BlockRequest>();
        if (peerBitfield == null || maxRequests <= 0)
        {
            return requests;
        }

        lock (_syncLock)
        {
            var candidateIndices = GetCandidatePieceIndices(peerBitfield, sequentialMode);

            foreach (var pieceIndex in candidateIndices)
            {
                var piece = _pieces[pieceIndex];
                if (piece.IsComplete || piece.Priority == 0)
                {
                    continue;
                }

                for (var blockIdx = 0; blockIdx < piece.TotalBlocks; blockIdx++)
                {
                    if (piece.BlockBitfield[blockIdx])
                    {
                        continue;
                    }

                    var blockKey = $"{pieceIndex}:{blockIdx}";
                    var isEndgame = IsEndgameMode();

                    if (!isEndgame && _inFlightBlocks.Contains(blockKey))
                    {
                        continue;
                    }

                    var offset = blockIdx * DefaultBlockSize;
                    var length = Math.Min(DefaultBlockSize, piece.Length - offset);

                    _inFlightBlocks.Add(blockKey);
                    requests.Add(new BlockRequest
                    {
                        PieceIndex = pieceIndex,
                        BlockOffset = offset,
                        BlockLength = length,
                        RequestedAt = DateTime.UtcNow
                    });

                    if (requests.Count >= maxRequests)
                    {
                        return requests;
                    }
                }
            }
        }

        return requests;
    }

    private List<int> GetCandidatePieceIndices(bool[] peerBitfield, bool sequentialMode)
    {
        var validPieces = new List<int>();
        var limit = Math.Min(_pieceCount, peerBitfield.Length);

        for (var i = 0; i < limit; i++)
        {
            if (peerBitfield[i] && !_pieces[i].IsComplete && _pieces[i].Priority > 0)
            {
                validPieces.Add(i);
            }
        }

        if (validPieces.Count == 0)
        {
            return validPieces;
        }

        if (sequentialMode)
        {
            // Sequential with Head / Tail priority
            var headPieces = validPieces.Take(4).ToList();
            var tailPieces = validPieces.TakeLast(2).ToList();
            var rest = validPieces.Skip(4).Take(Math.Max(0, validPieces.Count - 6)).ToList();

            var prioritized = new List<int>(headPieces);
            prioritized.AddRange(tailPieces);
            prioritized.AddRange(rest);
            return prioritized.Distinct().ToList();
        }

        // Rarest-First: sort by swarm availability, then by higher piece priority
        return validPieces
            .OrderByDescending(i => _pieces[i].Priority)
            .ThenBy(i => _swarmAvailability[i])
            .ToList();
    }

    public bool MarkBlockReceived(int pieceIndex, int blockOffset, int length)
    {
        lock (_syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= _pieceCount)
            {
                return false;
            }

            var piece = _pieces[pieceIndex];
            var blockIdx = blockOffset / DefaultBlockSize;

            if (blockIdx >= piece.TotalBlocks)
            {
                return false;
            }

            var blockKey = $"{pieceIndex}:{blockIdx}";
            _inFlightBlocks.Remove(blockKey);

            if (!piece.BlockBitfield[blockIdx])
            {
                piece.BlockBitfield[blockIdx] = true;
                piece.ReceivedBlocks++;

                if (piece.ReceivedBlocks >= piece.TotalBlocks)
                {
                    piece.IsComplete = true;
                    return true; // Whole piece complete, ready for hash verification
                }
            }

            return false;
        }
    }

    public void MarkPieceVerified(int pieceIndex)
    {
        lock (_syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < _pieceCount)
            {
                _pieces[pieceIndex].IsVerified = true;
                _pieces[pieceIndex].IsComplete = true;
            }
        }
    }

    public void MarkPieceCorrupt(int pieceIndex)
    {
        lock (_syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < _pieceCount)
            {
                var piece = _pieces[pieceIndex];
                piece.IsComplete = false;
                piece.IsVerified = false;
                piece.ReceivedBlocks = 0;
                Array.Clear(piece.BlockBitfield, 0, piece.BlockBitfield.Length);

                for (var i = 0; i < piece.TotalBlocks; i++)
                {
                    _inFlightBlocks.Remove($"{pieceIndex}:{i}");
                }
            }
        }
    }

    public void CancelBlock(int pieceIndex, int blockOffset)
    {
        lock (_syncLock)
        {
            var blockIdx = blockOffset / DefaultBlockSize;
            _inFlightBlocks.Remove($"{pieceIndex}:{blockIdx}");
        }
    }

    public bool[] GetBitfield()
    {
        lock (_syncLock)
        {
            var result = new bool[_pieceCount];
            for (var i = 0; i < _pieceCount; i++)
            {
                result[i] = _pieces[i].IsVerified;
            }

            return result;
        }
    }

    public double GetProgress()
    {
        lock (_syncLock)
        {
            if (_pieceCount == 0)
            {
                return 0.0;
            }

            var verifiedCount = _pieces.Count(p => p.IsVerified);
            return (double)verifiedCount / _pieceCount;
        }
    }
}
