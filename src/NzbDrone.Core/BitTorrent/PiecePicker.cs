// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;

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

    private readonly object syncLock = new();
    private readonly int pieceCount;
    private readonly int pieceLength;
    private readonly long totalSize;
    private readonly PieceState[] pieces;
    private readonly int[] swarmAvailability;
    private readonly Dictionary<long, DateTime> inFlightBlocks = new();

    private int totalActiveBlocks;
    private int remainingActiveBlocks;

    public PiecePicker(int pieceCount, int pieceLength, long totalSize, TimeSpan? requestTimeout = null)
    {
        if (pieceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceCount), "Piece count must be a positive integer.");
        }

        if (pieceLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pieceLength), "Piece length must be a positive integer.");
        }

        if (totalSize < 0)
        {
            throw new ArgumentException("Total size cannot be negative.", nameof(totalSize));
        }

        this.pieceCount = pieceCount;
        this.pieceLength = pieceLength;
        this.totalSize = totalSize;
        if (requestTimeout.HasValue)
        {
            this.RequestTimeout = requestTimeout.Value;
        }

        this.pieces = new PieceState[pieceCount];
        this.swarmAvailability = new int[pieceCount];

        for (var i = 0; i < pieceCount; i++)
        {
            var len = (i == pieceCount - 1) ? (int)(totalSize - ((long)i * pieceLength)) : pieceLength;
            if (len <= 0)
            {
                throw new ArgumentException($"Computed piece length for piece {i} must be positive, but was {len}.");
            }

            var totalBlocks = (int)Math.Ceiling((double)len / DefaultBlockSize);

            this.pieces[i] = new PieceState
            {
                Index = i,
                Length = len,
                TotalBlocks = totalBlocks,
                BlockBitfield = new bool[totalBlocks],
            };

            this.totalActiveBlocks += totalBlocks;
            this.remainingActiveBlocks += totalBlocks;
        }
    }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public int InFlightBlockCount
    {
        get
        {
            lock (this.syncLock)
            {
                return this.inFlightBlocks.Count;
            }
        }
    }

    public int PieceCount => this.pieceCount;

    public int PieceLength => this.pieceLength;

    public long TotalSize => this.totalSize;

    private static long GetBlockKey(int pieceIndex, int blockIndex) => ((long)pieceIndex << 32) | (uint)blockIndex;

    public void UpdatePeerAvailability(bool[] peerBitfield, bool isAdd)
    {
        if (!this.IsValidPeerBitfield(peerBitfield))
        {
            return;
        }

        lock (this.syncLock)
        {
            for (var i = 0; i < this.pieceCount; i++)
            {
                if (peerBitfield[i])
                {
                    this.swarmAvailability[i] = Math.Max(0, this.swarmAvailability[i] + (isAdd ? 1 : -1));
                }
            }
        }
    }

    private bool IsValidPeerBitfield(bool[] peerBitfield)
    {
        if (peerBitfield == null || peerBitfield.Length == 0)
        {
            return false;
        }

        if (this.pieceCount <= 0)
        {
            return false;
        }

        var maxAllowedBits = ((this.pieceCount + 7) / 8) * 8;
        if (peerBitfield.Length < this.pieceCount || peerBitfield.Length > maxAllowedBits)
        {
            return false;
        }

        for (var i = this.pieceCount; i < peerBitfield.Length; i++)
        {
            if (peerBitfield[i])
            {
                return false;
            }
        }

        return true;
    }

    public void SetPiecePriority(int pieceIndex, int priority)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount)
            {
                var piece = this.pieces[pieceIndex];
                if (piece.Priority != priority)
                {
                    if (piece.Priority > 0 && priority == 0)
                    {
                        this.totalActiveBlocks -= piece.TotalBlocks;
                        if (!piece.IsComplete)
                        {
                            this.remainingActiveBlocks -= piece.TotalBlocks - piece.ReceivedBlocks;
                        }
                    }
                    else if (piece.Priority == 0 && priority > 0)
                    {
                        this.totalActiveBlocks += piece.TotalBlocks;
                        if (!piece.IsComplete)
                        {
                            this.remainingActiveBlocks += piece.TotalBlocks - piece.ReceivedBlocks;
                        }
                    }

                    piece.Priority = priority;
                }
            }
        }
    }

    public bool IsEndgameMode()
    {
        lock (this.syncLock)
        {
            return this.IsEndgameModeCore();
        }
    }

    private bool IsEndgameModeCore()
    {
        if (this.remainingActiveBlocks <= 0)
        {
            return false;
        }

        return (this.totalActiveBlocks > 30 && this.remainingActiveBlocks <= 30) ||
               (this.inFlightBlocks.Count >= this.remainingActiveBlocks);
    }

    public List<BlockRequest> PickBlocks(bool[] peerBitfield, int maxRequests, bool sequentialMode = false)
    {
        var requests = new List<BlockRequest>();
        if (!this.IsValidPeerBitfield(peerBitfield) || maxRequests <= 0)
        {
            return requests;
        }

        lock (this.syncLock)
        {
            var candidateIndices = this.GetCandidatePieceIndices(peerBitfield, sequentialMode);

            foreach (var pieceIndex in candidateIndices)
            {
                var piece = this.pieces[pieceIndex];
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

                    var blockKey = GetBlockKey(pieceIndex, blockIdx);
                    var isEndgame = this.IsEndgameModeCore();

                    if (!isEndgame && this.inFlightBlocks.TryGetValue(blockKey, out var requestedAt))
                    {
                        if (DateTime.UtcNow - requestedAt < this.RequestTimeout)
                        {
                            continue;
                        }
                    }

                    var offset = blockIdx * DefaultBlockSize;
                    var length = Math.Min(DefaultBlockSize, piece.Length - offset);

                    var now = DateTime.UtcNow;
                    this.inFlightBlocks[blockKey] = now;
                    requests.Add(new BlockRequest
                    {
                        PieceIndex = pieceIndex,
                        BlockOffset = offset,
                        BlockLength = length,
                        RequestedAt = now,
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
        var limit = Math.Min(this.pieceCount, peerBitfield.Length);

        for (var i = 0; i < limit; i++)
        {
            if (peerBitfield[i] && !this.pieces[i].IsComplete && this.pieces[i].Priority > 0)
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
            var headThreshold = Math.Min(4, this.pieceCount);
            var tailThreshold = Math.Max(0, this.pieceCount - 2);

            var prioritized = new List<int>(validPieces.Count);
            for (var idx = 0; idx < validPieces.Count; idx++)
            {
                var pieceIdx = validPieces[idx];
                if (pieceIdx < headThreshold)
                {
                    prioritized.Add(pieceIdx);
                }
            }

            for (var idx = 0; idx < validPieces.Count; idx++)
            {
                var pieceIdx = validPieces[idx];
                if (pieceIdx >= tailThreshold && pieceIdx >= headThreshold)
                {
                    prioritized.Add(pieceIdx);
                }
            }

            for (var idx = 0; idx < validPieces.Count; idx++)
            {
                var pieceIdx = validPieces[idx];
                if (pieceIdx >= headThreshold && pieceIdx < tailThreshold)
                {
                    prioritized.Add(pieceIdx);
                }
            }

            return prioritized;
        }

        // Rarest-First: sort in-place by piece priority descending, then by swarm availability ascending
        validPieces.Sort((a, b) =>
        {
            var pCompare = this.pieces[b].Priority.CompareTo(this.pieces[a].Priority);
            if (pCompare != 0)
            {
                return pCompare;
            }

            return this.swarmAvailability[a].CompareTo(this.swarmAvailability[b]);
        });

        return validPieces;
    }

    public bool MarkBlockReceived(int pieceIndex, int blockOffset, int length)
    {
        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount)
            {
                return false;
            }

            if (blockOffset < 0 || blockOffset % DefaultBlockSize != 0)
            {
                return false;
            }

            var piece = this.pieces[pieceIndex];
            var blockIdx = blockOffset / DefaultBlockSize;

            if (blockIdx >= piece.TotalBlocks)
            {
                return false;
            }

            var expectedLength = Math.Min(DefaultBlockSize, piece.Length - blockOffset);
            if (length != expectedLength)
            {
                return false;
            }

            var blockKey = GetBlockKey(pieceIndex, blockIdx);
            this.inFlightBlocks.Remove(blockKey);

            if (!piece.BlockBitfield[blockIdx])
            {
                piece.BlockBitfield[blockIdx] = true;
                piece.ReceivedBlocks++;
                if (piece.Priority > 0)
                {
                    this.remainingActiveBlocks = Math.Max(0, this.remainingActiveBlocks - 1);
                }

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
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount)
            {
                var piece = this.pieces[pieceIndex];
                if (!piece.IsComplete)
                {
                    var unreceived = piece.TotalBlocks - piece.ReceivedBlocks;
                    if (piece.Priority > 0)
                    {
                        this.remainingActiveBlocks = Math.Max(0, this.remainingActiveBlocks - unreceived);
                    }

                    piece.ReceivedBlocks = piece.TotalBlocks;
                    for (var b = 0; b < piece.TotalBlocks; b++)
                    {
                        piece.BlockBitfield[b] = true;
                        this.inFlightBlocks.Remove(GetBlockKey(pieceIndex, b));
                    }

                    piece.IsComplete = true;
                }

                piece.IsVerified = true;
            }
        }
    }

    public void MarkPieceCorrupt(int pieceIndex)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount)
            {
                var piece = this.pieces[pieceIndex];
                if (piece.Priority > 0)
                {
                    this.remainingActiveBlocks += piece.ReceivedBlocks;
                }

                piece.IsComplete = false;
                piece.IsVerified = false;
                piece.ReceivedBlocks = 0;
                Array.Clear(piece.BlockBitfield, 0, piece.BlockBitfield.Length);

                for (var i = 0; i < piece.TotalBlocks; i++)
                {
                    this.inFlightBlocks.Remove(GetBlockKey(pieceIndex, i));
                }
            }
        }
    }

    public void CancelBlock(int pieceIndex, int blockOffset)
    {
        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount || blockOffset < 0 || blockOffset % DefaultBlockSize != 0)
            {
                return;
            }

            var blockIdx = blockOffset / DefaultBlockSize;
            this.inFlightBlocks.Remove(GetBlockKey(pieceIndex, blockIdx));
        }
    }

    public int PruneTimedOutRequests(TimeSpan? timeout = null)
    {
        lock (this.syncLock)
        {
            var effectiveTimeout = timeout ?? this.RequestTimeout;
            var cutoff = DateTime.UtcNow - effectiveTimeout;
            var expired = new List<long>();

            foreach (var kvp in this.inFlightBlocks)
            {
                if (kvp.Value <= cutoff)
                {
                    expired.Add(kvp.Key);
                }
            }

            foreach (var key in expired)
            {
                this.inFlightBlocks.Remove(key);
            }

            return expired.Count;
        }
    }

    public bool[] GetBitfield()
    {
        lock (this.syncLock)
        {
            var result = new bool[this.pieceCount];
            for (var i = 0; i < this.pieceCount; i++)
            {
                result[i] = this.pieces[i].IsVerified;
            }

            return result;
        }
    }

    public double GetProgress()
    {
        lock (this.syncLock)
        {
            if (this.pieceCount == 0)
            {
                return 0.0;
            }

            var verifiedCount = 0;
            for (var i = 0; i < this.pieceCount; i++)
            {
                if (this.pieces[i].IsVerified)
                {
                    verifiedCount++;
                }
            }

            return (double)verifiedCount / this.pieceCount;
        }
    }

    public int[] GetAvailability()
    {
        lock (this.syncLock)
        {
            var result = new int[this.pieceCount];
            Array.Copy(this.swarmAvailability, result, this.pieceCount);
            return result;
        }
    }

    public void SetAvailability(int[] availability)
    {
        if (availability == null)
        {
            return;
        }

        lock (this.syncLock)
        {
            var len = Math.Min(this.pieceCount, availability.Length);
            for (var i = 0; i < len; i++)
            {
                this.swarmAvailability[i] = Math.Max(0, availability[i]);
            }

            for (var i = len; i < this.pieceCount; i++)
            {
                this.swarmAvailability[i] = 0;
            }
        }
    }

    public void UpdateAvailability(int[] availability) => this.SetAvailability(availability);
}
