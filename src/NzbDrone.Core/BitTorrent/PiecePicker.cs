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

    public HashSet<string> ContributingPeers { get; } = new(StringComparer.OrdinalIgnoreCase);
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

    public bool SequentialMode { get; set; }

    public IReadOnlyCollection<int> PartialPieces => this.GetPartialPieces();

    public IReadOnlyCollection<int> GetPartialPieces()
    {
        lock (this.syncLock)
        {
            var result = new HashSet<int>();
            for (var i = 0; i < this.pieceCount; i++)
            {
                var piece = this.pieces[i];
                if (piece != null && piece.ReceivedBlocks > 0 && !piece.IsComplete)
                {
                    result.Add(i);
                }
            }

            foreach (var key in this.inFlightBlocks.Keys)
            {
                var colonIndex = key.IndexOf(':');
                if (colonIndex > 0 && int.TryParse(key.AsSpan(0, colonIndex), out var pieceIdx))
                {
                    if (pieceIdx >= 0 && pieceIdx < this.pieceCount && this.pieces[pieceIdx] != null && !this.pieces[pieceIdx].IsComplete)
                    {
                        result.Add(pieceIdx);
                    }
                }
            }

            return result;
        }
    }

    public int InFlightBlockCount
    {
        get
        {
            lock (this.syncLock)
            {
                var count = 0;
                var staleKeys = new List<string>();

                foreach (var (key, _) in this.inFlightBlocks)
                {
                    var colonIndex = key.IndexOf(':');
                    if (colonIndex > 0 &&
                        int.TryParse(key.AsSpan(0, colonIndex), out var pieceIdx) &&
                        int.TryParse(key.AsSpan(colonIndex + 1), out var blockIdx))
                    {
                        if (pieceIdx >= 0 && pieceIdx < this.pieceCount)
                        {
                            var piece = this.pieces[pieceIdx];
                            if (piece != null && !piece.IsComplete && !piece.IsVerified &&
                                blockIdx >= 0 && blockIdx < piece.TotalBlocks &&
                                piece.BlockBitfield != null && !piece.BlockBitfield[blockIdx])
                            {
                                count++;
                                continue;
                            }
                        }
                    }

                    staleKeys.Add(key);
                }

                foreach (var staleKey in staleKeys)
                {
                    this.inFlightBlocks.Remove(staleKey);
                }

                return count;
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
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount && this.pieces[pieceIndex] != null)
            {
                this.pieces[pieceIndex].Priority = priority;
            }
        }
    }

    public int GetPiecePriority(int pieceIndex)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount && this.pieces[pieceIndex] != null)
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
            var activePieces = this.pieces.Where(p => p != null && p.Priority > 0 && !p.IsComplete).ToList();
            var remainingBlocks = activePieces.Sum(p => Math.Max(0, p.TotalBlocks - p.ReceivedBlocks));
            if (remainingBlocks <= 0)
            {
                return false;
            }

            var totalActivePieces = this.pieces.Count(p => p != null && p.Priority > 0);
            if (totalActivePieces == 0)
            {
                return false;
            }

            var totalBlocks = this.pieces.Where(p => p != null && p.Priority > 0).Sum(p => p.TotalBlocks);
            var remainingPieces = activePieces.Count;
            var totalActiveBytes = this.pieces.Where(p => p != null && p.Priority > 0).Sum(p => (long)p.Length);
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
            var isEndgame = this.IsEndgameMode();

            foreach (var pieceIndex in candidateIndices)
            {
                if (pieceIndex < 0 || pieceIndex >= this.pieceCount)
                {
                    continue;
                }

                var piece = this.pieces[pieceIndex];
                if (piece == null || piece.IsComplete || piece.Priority == 0)
                {
                    continue;
                }

                for (var blockIdx = 0; blockIdx < piece.TotalBlocks; blockIdx++)
                {
                    if (piece.BlockBitfield == null || blockIdx >= piece.BlockBitfield.Length || piece.BlockBitfield[blockIdx])
                    {
                        continue;
                    }

                    var blockKey = $"{pieceIndex}:{blockIdx}";
                    var now = DateTime.UtcNow;

                    if (this.inFlightBlocks.TryGetValue(blockKey, out var existingInfo) && existingInfo != null)
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
                            if (!string.IsNullOrWhiteSpace(peerId) && existingInfo.PeerRequests.ContainsKey(peerId))
                            {
                                continue;
                            }
                        }
                    }

                    var offset = blockIdx * DefaultBlockSize;
                    if (offset >= piece.Length)
                    {
                        continue;
                    }

                    var length = Math.Min(DefaultBlockSize, piece.Length - offset);
                    if (length <= 0)
                    {
                        continue;
                    }

                    if (!this.inFlightBlocks.TryGetValue(blockKey, out var info) || info == null)
                    {
                        info = new BlockInFlightInfo { FirstRequestedAt = now };
                        this.inFlightBlocks[blockKey] = info;
                    }

                    if (!string.IsNullOrWhiteSpace(peerId))
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

        if (this.totalSize > 2L * 1024 * 1024 && this.pieceLength > 0)
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
        var tailStart = Math.Clamp(Math.Max(headPieces, this.pieceCount - tailPieces), 0, this.pieceCount);

        return (headPieces, tailStart);
    }

    private List<int> GetCandidatePieceIndices(bool[] peerBitfield, bool sequentialMode)
    {
        var validPieces = new List<int>();
        if (peerBitfield == null)
        {
            return validPieces;
        }

        var limit = Math.Min(this.pieceCount, peerBitfield.Length);

        for (var i = 0; i < limit; i++)
        {
            if (peerBitfield[i] && this.pieces[i] != null && !this.pieces[i].IsComplete && this.pieces[i].Priority > 0)
            {
                validPieces.Add(i);
            }
        }

        if (validPieces.Count == 0)
        {
            return validPieces;
        }

        if (sequentialMode || this.SequentialMode)
        {
            // Sequential with dynamic Head / Tail priority, respecting piece priority weights
            var (headThreshold, tailThreshold) = this.CalculateSequentialHeadTailThresholds();

            var prioritized = new List<int>();

            foreach (var group in validPieces.GroupBy(i => this.pieces[i].Priority).OrderByDescending(g => g.Key))
            {
                var groupPieces = group.ToList();
                var headPieces = groupPieces.Where(i => i < headThreshold).OrderBy(i => i);
                var tailPieces = groupPieces.Where(i => i >= tailThreshold).OrderBy(i => i);
                var rest = groupPieces.Where(i => i >= headThreshold && i < tailThreshold).OrderBy(i => i);

                prioritized.AddRange(headPieces);
                prioritized.AddRange(tailPieces);
                prioritized.AddRange(rest);
            }

            return prioritized.Distinct().ToList();
        }

        // Rarest-First: sort by higher piece priority, then by rarest swarm availability, with randomized tie-breaking for equal rarity
        return validPieces
            .OrderByDescending(i => this.pieces[i].Priority)
            .ThenBy(i => this.swarmAvailability[i])
            .ThenBy(_ => Random.Shared.Next())
            .ToList();
    }

    public bool MarkBlockReceived(int pieceIndex, int blockOffset, int length)
    {
        return this.MarkBlockReceived(pieceIndex, blockOffset, length, null, out _);
    }

    public bool MarkBlockReceived(int pieceIndex, int blockOffset, int length, string receivedFromPeerId, out List<string> cancelledPeers)
    {
        cancelledPeers = new List<string>();
        var isPieceComplete = false;
        var cleanBlockOffset = blockOffset;
        var cleanLength = length;

        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount || blockOffset < 0)
            {
                return false;
            }

            if (blockOffset % DefaultBlockSize != 0)
            {
                return false;
            }

            var piece = this.pieces[pieceIndex];
            if (piece == null || blockOffset >= piece.Length)
            {
                return false;
            }

            var blockIdx = blockOffset / DefaultBlockSize;
            if (blockIdx < 0 || blockIdx >= piece.TotalBlocks)
            {
                return false;
            }

            cleanBlockOffset = blockIdx * DefaultBlockSize;
            var expectedLength = Math.Min(DefaultBlockSize, piece.Length - cleanBlockOffset);
            if (length <= 0 || length != expectedLength)
            {
                return false;
            }

            cleanLength = expectedLength;

            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info) && info != null)
            {
                if (info.PeerRequests != null)
                {
                    foreach (var peer in info.PeerRequests.Keys)
                    {
                        if (!string.IsNullOrWhiteSpace(peer) &&
                            !string.Equals(peer, receivedFromPeerId, StringComparison.OrdinalIgnoreCase))
                        {
                            cancelledPeers.Add(peer);
                        }
                    }
                }

                this.inFlightBlocks.Remove(blockKey);
            }

            if (piece.BlockBitfield != null && blockIdx < piece.BlockBitfield.Length && !piece.BlockBitfield[blockIdx])
            {
                piece.BlockBitfield[blockIdx] = true;
                piece.ReceivedBlocks++;

                if (!string.IsNullOrWhiteSpace(receivedFromPeerId))
                {
                    piece.ContributingPeers.Add(receivedFromPeerId);
                }

                if (piece.ReceivedBlocks >= piece.TotalBlocks)
                {
                    piece.IsComplete = true;
                    isPieceComplete = true;
                }
            }
            else if (piece.ReceivedBlocks >= piece.TotalBlocks)
            {
                isPieceComplete = piece.IsComplete;
            }
        }

        var cleanCancelledPeers = cancelledPeers
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        cancelledPeers = cleanCancelledPeers;

        if (cleanCancelledPeers.Count > 0)
        {
            this.BlockCancelled?.Invoke(new BlockCancelledEventArgs
            {
                PieceIndex = pieceIndex,
                BlockOffset = cleanBlockOffset,
                BlockLength = cleanLength,
                CancelledPeerIds = new List<string>(cleanCancelledPeers),
            });
        }

        return isPieceComplete;
    }

    public List<string> GetDuplicateInFlightPeers(int pieceIndex, int blockOffset, string excludingPeerId = null)
    {
        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount || blockOffset < 0)
            {
                return new List<string>();
            }

            var piece = this.pieces[pieceIndex];
            if (piece == null || blockOffset >= piece.Length)
            {
                return new List<string>();
            }

            var blockIdx = blockOffset / DefaultBlockSize;
            if (blockIdx < 0 || blockIdx >= piece.TotalBlocks)
            {
                return new List<string>();
            }

            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info) && info?.PeerRequests != null)
            {
                return info.PeerRequests.Keys
                    .Where(p => !string.IsNullOrWhiteSpace(p) && !string.Equals(p, excludingPeerId, StringComparison.OrdinalIgnoreCase))
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
                var piece = this.pieces[pieceIndex];
                if (piece != null)
                {
                    piece.IsVerified = true;
                    piece.IsComplete = true;
                    piece.ContributingPeers.Clear();

                    for (var i = 0; i < piece.TotalBlocks; i++)
                    {
                        this.inFlightBlocks.Remove($"{pieceIndex}:{i}");
                    }
                }
            }
        }
    }

    public IReadOnlyCollection<string> GetContributingPeers(int pieceIndex)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount && this.pieces[pieceIndex] != null)
            {
                return new List<string>(this.pieces[pieceIndex].ContributingPeers);
            }

            return Array.Empty<string>();
        }
    }

    public List<string> MarkPieceCorrupt(int pieceIndex)
    {
        lock (this.syncLock)
        {
            if (pieceIndex >= 0 && pieceIndex < this.pieceCount)
            {
                var piece = this.pieces[pieceIndex];
                if (piece != null)
                {
                    var contributors = new List<string>(piece.ContributingPeers);
                    piece.IsComplete = false;
                    piece.IsVerified = false;
                    piece.ReceivedBlocks = 0;
                    piece.ContributingPeers.Clear();
                    if (piece.BlockBitfield != null)
                    {
                        Array.Clear(piece.BlockBitfield, 0, piece.BlockBitfield.Length);
                    }

                    for (var i = 0; i < piece.TotalBlocks; i++)
                    {
                        this.inFlightBlocks.Remove($"{pieceIndex}:{i}");
                    }

                    return contributors;
                }
            }

            return new List<string>();
        }
    }

    public void CancelRequest(int pieceIndex, int blockOffset, string peerId = null)
    {
        this.CancelBlock(pieceIndex, blockOffset, DefaultBlockSize, peerId);
    }

    public void CancelRequest(int pieceIndex, int blockOffset, int length, string peerId = null)
    {
        this.CancelBlock(pieceIndex, blockOffset, length, peerId);
    }

    public void CancelBlock(int pieceIndex, int blockOffset, string peerId = null)
    {
        this.CancelBlock(pieceIndex, blockOffset, DefaultBlockSize, peerId);
    }

    public void CancelBlock(int pieceIndex, int blockOffset, int length, string peerId = null)
    {
        var cancelledPeers = new List<string>();
        var cleanBlockOffset = blockOffset;
        var cleanLength = length;

        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount || blockOffset < 0)
            {
                return;
            }

            var piece = this.pieces[pieceIndex];
            if (piece == null || blockOffset >= piece.Length)
            {
                return;
            }

            var blockIdx = blockOffset / DefaultBlockSize;
            if (blockIdx < 0 || blockIdx >= piece.TotalBlocks)
            {
                return;
            }

            cleanBlockOffset = blockIdx * DefaultBlockSize;
            cleanLength = Math.Min(length > 0 ? length : DefaultBlockSize, piece.Length - cleanBlockOffset);

            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info) && info != null)
            {
                if (!string.IsNullOrWhiteSpace(peerId))
                {
                    if (info.PeerRequests.Remove(peerId))
                    {
                        cancelledPeers.Add(peerId);
                    }

                    if (info.PeerRequests.Count == 0 || !this.IsEndgameMode())
                    {
                        this.inFlightBlocks.Remove(blockKey);
                    }
                }
                else
                {
                    if (info.PeerRequests != null)
                    {
                        cancelledPeers.AddRange(info.PeerRequests.Keys);
                    }

                    this.inFlightBlocks.Remove(blockKey);
                }
            }
        }

        var cleanCancelledPeers = cancelledPeers
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleanCancelledPeers.Count > 0)
        {
            this.BlockCancelled?.Invoke(new BlockCancelledEventArgs
            {
                PieceIndex = pieceIndex,
                BlockOffset = cleanBlockOffset,
                BlockLength = cleanLength,
                CancelledPeerIds = cleanCancelledPeers,
            });
        }
    }

    public void RejectRequest(int pieceIndex, int blockOffset, string peerId = null)
    {
        this.RejectRequest(pieceIndex, blockOffset, DefaultBlockSize, peerId);
    }

    public void RejectRequest(int pieceIndex, int blockOffset, int length, string peerId = null)
    {
        lock (this.syncLock)
        {
            if (pieceIndex < 0 || pieceIndex >= this.pieceCount || blockOffset < 0)
            {
                return;
            }

            var piece = this.pieces[pieceIndex];
            if (piece == null || blockOffset >= piece.Length)
            {
                return;
            }

            var blockIdx = blockOffset / DefaultBlockSize;
            if (blockIdx < 0 || blockIdx >= piece.TotalBlocks)
            {
                return;
            }

            var blockKey = $"{pieceIndex}:{blockIdx}";
            if (this.inFlightBlocks.TryGetValue(blockKey, out var info) && info != null)
            {
                if (!string.IsNullOrWhiteSpace(peerId))
                {
                    info.PeerRequests.Remove(peerId);
                    if (info.PeerRequests.Count == 0 || !this.IsEndgameMode())
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

    public int PruneTimedOutRequests(TimeSpan? timeout = null)
    {
        lock (this.syncLock)
        {
            var effectiveTimeout = timeout ?? this.RequestTimeout;
            var cutoff = DateTime.UtcNow - effectiveTimeout;
            var expired = this.inFlightBlocks
                .Where(kvp => kvp.Value == null || kvp.Value.FirstRequestedAt <= cutoff)
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
                result[i] = this.pieces[i]?.IsVerified ?? false;
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

            var activeCount = this.pieces.Count(p => p != null && p.Priority > 0);
            if (activeCount == 0)
            {
                return 0.0;
            }

            var verifiedCount = this.pieces.Count(p => p != null && p.Priority > 0 && p.IsVerified);
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
