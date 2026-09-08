// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.BitTorrent;

public class BlockRequest
{
    public int PieceIndex { get; set; }

    public int BlockOffset { get; set; }

    public int BlockLength { get; set; }

    public DateTime RequestedAt { get; set; }
}

public class BlockCancelledEventArgs : EventArgs
{
    public int PieceIndex { get; set; }

    public int BlockOffset { get; set; }

    public int BlockLength { get; set; }

    public List<string> CancelledPeerIds { get; set; } = new();
}

public class BlockInFlightInfo
{
    public DateTime FirstRequestedAt { get; set; }

    public Dictionary<string, DateTime> PeerRequests { get; } = new(StringComparer.OrdinalIgnoreCase);
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
    private readonly IConfigService configService;
    private readonly PieceState[] pieces;
    private readonly int[] swarmAvailability;
    private readonly Dictionary<string, BlockInFlightInfo> inFlightBlocks = new();
    private TimeSpan? requestTimeout;

    public PiecePicker(int pieceCount, int pieceLength, long totalSize, TimeSpan? requestTimeout = null, IConfigService configService = null)
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
        this.configService = configService;
        if (requestTimeout.HasValue)
        {
            this.requestTimeout = requestTimeout.Value;
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
        }
    }

    public event Action<BlockCancelledEventArgs> BlockCancelled;

    public TimeSpan RequestTimeout
    {
        get
        {
            if (this.requestTimeout.HasValue)
            {
                return this.requestTimeout.Value;
            }

            if (this.configService != null && this.configService.StaleRequestTimeoutSeconds > 0)
            {
                return TimeSpan.FromSeconds(this.configService.StaleRequestTimeoutSeconds);
            }

            return TimeSpan.FromSeconds(20);
        }
        set => this.requestTimeout = value;
    }

    public bool EndGamePickerEnabled { get; set; } = true;

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
                this.pieces[pieceIndex].Priority = priority;
            }
        }
    }

    public int GetPiecePriority(int pieceIndex)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount)
            {
                return this.pieces[pieceIndex].Priority;
            }

            return 0;
        }
    }

    public bool IsEndgameMode()
    {
        var isEnabled = this.configService?.EndGamePickerEnabled ?? this.EndGamePickerEnabled;
        if (!isEnabled)
        {
            return false;
        }

        lock (this.syncLock)
        {
            var activePieces = this.pieces.Where(p => p.Priority > 0 && !p.IsComplete).ToList();
            var remainingBlocks = activePieces.Sum(p => p.TotalBlocks - p.ReceivedBlocks);
            if (remainingBlocks <= 0)
            {
                return false;
            }

            var totalActivePieces = this.pieces.Count(p => p.Priority > 0);
            if (totalActivePieces == 0)
            {
                return false;
            }

            var totalBlocks = this.pieces.Where(p => p.Priority > 0).Sum(p => p.TotalBlocks);
            var remainingPieces = activePieces.Count;
            var totalActiveBytes = this.pieces.Where(p => p.Priority > 0).Sum(p => (long)p.Length);
            var remainingBytes = activePieces.Sum(p => (long)(p.TotalBlocks - p.ReceivedBlocks) * DefaultBlockSize > p.Length ? p.Length : (long)(p.TotalBlocks - p.ReceivedBlocks) * DefaultBlockSize);

            // Endgame mode triggers when:
            // 1. All remaining blocks are currently in flight across peers
            // 2. Remaining active pieces <= 20 (and total pieces > 20)
            // 3. Dynamic percentage scaling: remaining bytes or blocks <= 2% of total active content
            var isBlockPercentageTriggered = totalBlocks > 0 && ((double)remainingBlocks / totalBlocks) <= 0.02 && totalBlocks > remainingBlocks;
            var isBytePercentageTriggered = totalActiveBytes > 0 && remainingBytes < totalActiveBytes && ((double)remainingBytes / totalActiveBytes) <= 0.02;
            var isNearEnd = (totalActivePieces > 20 && remainingPieces <= 20) || isBlockPercentageTriggered || isBytePercentageTriggered;

            return isNearEnd || (this.inFlightBlocks.Count >= remainingBlocks);
        }
    }

    public List<BlockRequest> PickBlocks(
        bool[] peerBitfield,
        int maxRequests,
        bool sequentialMode = false,
        string peerId = null,
        bool isSnubbed = false)
    {
        var requests = new List<BlockRequest>();
        if (!this.IsValidPeerBitfield(peerBitfield) || maxRequests <= 0)
        {
            return requests;
        }

        // When a peer is snubbed (inactive > 60s), only probe with at most 1 block request
        var effectiveMaxRequests = isSnubbed ? Math.Min(1, maxRequests) : maxRequests;

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

                    var blockKey = $"{pieceIndex}:{blockIdx}";
                    var isEndgame = this.IsEndgameMode();
                    var now = DateTime.UtcNow;

                    if (this.inFlightBlocks.TryGetValue(blockKey, out var existingInfo))
                    {
                        if (!isEndgame)
                        {
                            var isTimedOut = now - existingInfo.FirstRequestedAt >= this.RequestTimeout;
                            if (!isTimedOut)
                            {
                                continue;
                            }

                            existingInfo.FirstRequestedAt = now;
                            existingInfo.PeerRequests.Clear();
                        }
                        else
                        {
                            // In endgame mode: avoid sending duplicate in-flight requests to the exact same peer
                            if (!string.IsNullOrEmpty(peerId) && existingInfo.PeerRequests.ContainsKey(peerId))
                            {
                                continue;
                            }
                        }
                    }

                    var offset = blockIdx * DefaultBlockSize;
                    var length = Math.Min(DefaultBlockSize, piece.Length - offset);

                    if (!this.inFlightBlocks.TryGetValue(blockKey, out var info))
                    {
                        info = new BlockInFlightInfo { FirstRequestedAt = now };
                        this.inFlightBlocks[blockKey] = info;
                    }

                    if (!string.IsNullOrEmpty(peerId))
                    {
                        info.PeerRequests[peerId] = now;
                    }

                    requests.Add(new BlockRequest
                    {
                        PieceIndex = pieceIndex,
                        BlockOffset = offset,
                        BlockLength = length,
                        RequestedAt = now,
                    });

                    if (requests.Count >= effectiveMaxRequests)
                    {
                        return requests;
                    }
                }
            }
        }

        return requests;
    }

    private (int HeadThreshold, int TailThreshold) CalculateSequentialHeadTailThresholds()
    {
        if (this.pieceCount <= 2)
        {
            return (this.pieceCount, this.pieceCount);
        }

        if (this.pieceCount <= 4)
        {
            return (1, Math.Max(1, this.pieceCount - 2));
        }

        if (this.pieceCount <= 6)
        {
            return (2, Math.Max(2, this.pieceCount - 2));
        }

        // For small files (< 2MB) or standard small piece sets, default to 4 head pieces and 2 tail pieces
        int headPieces;
        int tailPieces;

        if (this.totalSize > 2L * 1024 * 1024)
        {
            // Dynamic byte-size calculation: prioritize 2MB to 8MB head and 1MB to 4MB tail
            var targetHeadBytes = Math.Min(8L * 1024 * 1024, Math.Max(2L * 1024 * 1024, (long)(this.totalSize * 0.05)));
            var targetTailBytes = Math.Min(4L * 1024 * 1024, Math.Max(1L * 1024 * 1024, (long)(this.totalSize * 0.025)));

            var computedHead = (int)Math.Ceiling((double)targetHeadBytes / this.pieceLength);
            var computedTail = (int)Math.Ceiling((double)targetTailBytes / this.pieceLength);

            var maxHead = Math.Max(4, this.pieceCount / 10);
            var maxTail = Math.Max(2, this.pieceCount / 20);

            headPieces = Math.Min(Math.Max(4, computedHead), Math.Min(maxHead, this.pieceCount - 3));
            tailPieces = Math.Min(Math.Max(2, computedTail), Math.Min(maxTail, this.pieceCount - headPieces - 1));
        }
        else
        {
            headPieces = 4;
            tailPieces = 2;
        }

        headPieces = Math.Max(1, Math.Min(headPieces, this.pieceCount - 2));
        tailPieces = Math.Max(1, Math.Min(tailPieces, this.pieceCount - headPieces));
        var tailStart = Math.Max(headPieces, this.pieceCount - tailPieces);

        return (headPieces, tailStart);
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
            // Sequential with dynamic Head / Tail priority
            var (headThreshold, tailThreshold) = this.CalculateSequentialHeadTailThresholds();

            var headPieces = validPieces.Where(i => i < headThreshold).OrderBy(i => i);
            var tailPieces = validPieces.Where(i => i >= tailThreshold).OrderBy(i => i);
            var rest = validPieces.Where(i => i >= headThreshold && i < tailThreshold).OrderBy(i => i);

            var prioritized = new List<int>();
            prioritized.AddRange(headPieces);
            prioritized.AddRange(tailPieces);
            prioritized.AddRange(rest);
            return prioritized.Distinct().ToList();
        }

        // Rarest-First: sort by swarm availability, then by higher piece priority
        return validPieces
            .OrderByDescending(i => this.pieces[i].Priority)
            .ThenBy(i => this.swarmAvailability[i])
            .ToList();
    }

    public bool MarkBlockReceived(int pieceIndex, int blockOffset, int length)
    {
        return this.MarkBlockReceived(pieceIndex, blockOffset, length, null, out _);
    }

    public bool MarkBlockReceived(int pieceIndex, int blockOffset, int length, string receivedFromPeerId, out List<string> cancelledPeers)
    {
        cancelledPeers = new List<string>();

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

            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info))
            {
                foreach (var peer in info.PeerRequests.Keys)
                {
                    if (!string.IsNullOrEmpty(peer) && !string.Equals(peer, receivedFromPeerId, StringComparison.OrdinalIgnoreCase))
                    {
                        cancelledPeers.Add(peer);
                    }
                }

                this.inFlightBlocks.Remove(blockKey);
            }

            if (cancelledPeers.Count > 0)
            {
                this.BlockCancelled?.Invoke(new BlockCancelledEventArgs
                {
                    PieceIndex = pieceIndex,
                    BlockOffset = blockOffset,
                    BlockLength = length,
                    CancelledPeerIds = new List<string>(cancelledPeers),
                });
            }

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

    public List<string> GetDuplicateInFlightPeers(int pieceIndex, int blockOffset, string excludingPeerId = null)
    {
        lock (this.syncLock)
        {
            var blockIdx = blockOffset / DefaultBlockSize;
            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info))
            {
                return info.PeerRequests.Keys
                    .Where(p => !string.IsNullOrEmpty(p) && !string.Equals(p, excludingPeerId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return new List<string>();
        }
    }

    public void MarkPieceVerified(int pieceIndex)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount)
            {
                this.pieces[pieceIndex].IsVerified = true;
                this.pieces[pieceIndex].IsComplete = true;
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
                piece.IsComplete = false;
                piece.IsVerified = false;
                piece.ReceivedBlocks = 0;
                Array.Clear(piece.BlockBitfield, 0, piece.BlockBitfield.Length);

                for (var i = 0; i < piece.TotalBlocks; i++)
                {
                    this.inFlightBlocks.Remove($"{pieceIndex}:{i}");
                }
            }
        }
    }

    public void CancelBlock(int pieceIndex, int blockOffset, string peerId = null)
    {
        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount || blockOffset < 0 || blockOffset % DefaultBlockSize != 0)
            {
                return;
            }

            var blockIdx = blockOffset / DefaultBlockSize;
            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info))
            {
                if (!string.IsNullOrEmpty(peerId))
                {
                    info.PeerRequests.Remove(peerId);
                    if (info.PeerRequests.Count == 0)
                    {
                        this.inFlightBlocks.Remove(blockKey);
                    }
                }
                else
                {
                    this.inFlightBlocks.Remove(blockKey);
                }
            }
        }
    }

    public void RejectRequest(int pieceIndex, int blockOffset, string peerId = null)
    {
        this.CancelBlock(pieceIndex, blockOffset, peerId);
    }

    public void RejectRequest(int pieceIndex, int blockOffset, int length, string peerId = null)
    {
        this.CancelBlock(pieceIndex, blockOffset, peerId);
    }

    public int PruneTimedOutRequests(TimeSpan? timeout = null)
    {
        lock (this.syncLock)
        {
            var effectiveTimeout = timeout ?? this.RequestTimeout;
            var cutoff = DateTime.UtcNow - effectiveTimeout;
            var expired = this.inFlightBlocks
                .Where(kvp => kvp.Value.FirstRequestedAt <= cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

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

            var activeCount = this.pieces.Count(p => p.Priority > 0);
            if (activeCount == 0)
            {
                return 0.0;
            }

            var verifiedCount = this.pieces.Count(p => p.Priority > 0 && p.IsVerified);
            return (double)verifiedCount / activeCount;
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
