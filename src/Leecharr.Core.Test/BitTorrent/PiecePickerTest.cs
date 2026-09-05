// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class PiecePickerTest
{
    #region Basic & Construction Tests

    [Test]
    public void Constructor_InitializesPiecesAndPropertiesCorrectly()
    {
        // 10 pieces, 32KB each, total 320KB
        var picker = new PiecePicker(10, 32768, 327680);

        picker.PieceCount.Should().Be(10);
        picker.PieceLength.Should().Be(32768);
        picker.TotalSize.Should().Be(327680);
        picker.GetProgress().Should().Be(0.0);

        var bitfield = picker.GetBitfield();
        bitfield.Should().HaveCount(10);
        bitfield.All(b => !b).Should().BeTrue();
    }

    [Test]
    public void Constructor_HandlesNonUniformLastPieceSize()
    {
        // 3 pieces, 16384 each, total size 35000 (piece 0: 16384, piece 1: 16384, piece 2: 2232)
        var picker = new PiecePicker(3, 16384, 35000);

        picker.PieceCount.Should().Be(3);

        var fullBitfield = new[] { true, true, true };
        var requests = picker.PickBlocks(fullBitfield, 10);

        // Piece 0: 1 block of 16384
        // Piece 1: 1 block of 16384
        // Piece 2: 1 block of 2232
        requests.Should().HaveCount(3);
        requests[2].PieceIndex.Should().Be(2);
        requests[2].BlockLength.Should().Be(2232);
    }

    [Test]
    public void PickBlocks_WithNullOrEmptyInputs_ReturnsEmptyList()
    {
        var picker = new PiecePicker(5, 16384, 81920);

        picker.PickBlocks(null!, 5).Should().BeEmpty();
        picker.PickBlocks(new bool[5], 0).Should().BeEmpty();
        picker.PickBlocks(new bool[5], -1).Should().BeEmpty();
        picker.PickBlocks(new bool[5], 5).Should().BeEmpty();
    }

    #endregion

    #region Rarest-First Tests

    [Test]
    public void PickBlocks_PicksRarestPiecesFirst_InMultiPeerSwarm()
    {
        // 10 pieces, 16KB each
        var picker = new PiecePicker(10, 16384, 163840);

        // Peer 1 has pieces 0..9 (availability = 1 for all)
        var peer1 = Enumerable.Repeat(true, 10).ToArray();
        picker.UpdatePeerAvailability(peer1, isAdd: true);

        // Peer 2 has pieces 0, 1, 2, 3 (availability for 0..3 is 2)
        var peer2 = new bool[10];
        peer2[0] = peer2[1] = peer2[2] = peer2[3] = true;
        picker.UpdatePeerAvailability(peer2, isAdd: true);

        // Peer 3 has pieces 0, 1 (availability for 0..1 is 3)
        var peer3 = new bool[10];
        peer3[0] = peer3[1] = true;
        picker.UpdatePeerAvailability(peer3, isAdd: true);

        // Availability:
        // Pieces 0, 1 = 3 (most common)
        // Pieces 2, 3 = 2
        // Pieces 4, 5, 6, 7, 8, 9 = 1 (rarest)

        // Peer 4 has pieces 1 (common) and 7 (rare)
        var peer4 = new bool[10];
        peer4[1] = true;
        peer4[7] = true;

        var requests = picker.PickBlocks(peer4, 2);

        requests.Should().HaveCount(2);
        // Rarest (Piece 7) must be picked before Piece 1
        requests[0].PieceIndex.Should().Be(7);
        requests[1].PieceIndex.Should().Be(1);
    }

    [Test]
    public void PickBlocks_WhenPeerLeaves_RecalculatesRarestFirstOrder()
    {
        var picker = new PiecePicker(5, 16384, 81920);

        // Peer 1 has piece 0 and 1
        var peer1 = new bool[] { true, true, false, false, false };
        picker.UpdatePeerAvailability(peer1, isAdd: true);

        // Peer 2 has piece 1
        var peer2 = new bool[] { false, true, false, false, false };
        picker.UpdatePeerAvailability(peer2, isAdd: true);

        // Piece 0 rarity: 1, Piece 1 rarity: 2
        // Peer 1 disconnects:
        picker.UpdatePeerAvailability(peer1, isAdd: false);

        // Now Piece 0 rarity: 0, Piece 1 rarity: 1
        // Peer 3 connects with pieces 0 and 1
        var peer3 = new bool[] { true, true, false, false, false };
        var requests = picker.PickBlocks(peer3, 2);

        requests.Should().HaveCount(2);
        // Piece 0 has rarity 0, so it is rarer than Piece 1 (rarity 1)
        requests[0].PieceIndex.Should().Be(0);
        requests[1].PieceIndex.Should().Be(1);
    }

    [Test]
    public void PickBlocks_PrioritizesHigherPriorityPiecesOverRarity()
    {
        var picker = new PiecePicker(5, 16384, 81920);

        // Swarm: piece 0 has rarity 1, piece 4 has rarity 5
        var peerSwarm = Enumerable.Repeat(true, 5).ToArray();
        for (var i = 0; i < 5; i++)
        {
            picker.UpdatePeerAvailability(peerSwarm, isAdd: true);
        }

        // Set high priority on piece 4 (even though it is common)
        picker.SetPiecePriority(4, 3); // Max priority
        picker.SetPiecePriority(0, 1); // Normal priority

        var requests = picker.PickBlocks(peerSwarm, 1);

        requests.Should().HaveCount(1);
        requests[0].PieceIndex.Should().Be(4);
    }

    [Test]
    public void PickBlocks_SkipsPiecesWithPriorityZero()
    {
        var picker = new PiecePicker(5, 16384, 81920);
        picker.SetPiecePriority(0, 0); // Skip piece 0
        picker.SetPiecePriority(2, 0); // Skip piece 2

        var fullBitfield = Enumerable.Repeat(true, 5).ToArray();
        var requests = picker.PickBlocks(fullBitfield, 10);

        requests.Should().NotBeEmpty();
        requests.Any(r => r.PieceIndex == 0).Should().BeFalse();
        requests.Any(r => r.PieceIndex == 2).Should().BeFalse();
    }

    #endregion

    #region Sequential Mode Tests

    [Test]
    public void PickBlocks_SequentialMode_PrioritizesHeadAndTailPiecesForMediaInspection()
    {
        // 50 pieces, 16KB each
        var picker = new PiecePicker(50, 16384, 819200);
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        // Pick 6 blocks in sequential mode: should pick head pieces (0, 1, 2, 3) and tail pieces (48, 49)
        var requests = picker.PickBlocks(fullBitfield, 6, sequentialMode: true);

        requests.Should().HaveCount(6);

        var pieceIndices = requests.Select(r => r.PieceIndex).Distinct().ToList();

        // First 4 pieces (head: 0, 1, 2, 3)
        pieceIndices.Should().Contain(new[] { 0, 1, 2, 3 });
        // Last 2 pieces (tail: 48, 49)
        pieceIndices.Should().Contain(new[] { 48, 49 });
    }

    [Test]
    public void PickBlocks_SequentialMode_RequestsSequentialInteriorAfterHeadAndTailComplete()
    {
        // 10 pieces, 16KB each
        var picker = new PiecePicker(10, 16384, 163840);
        var fullBitfield = Enumerable.Repeat(true, 10).ToArray();

        // Complete head pieces (0, 1, 2, 3) and tail pieces (8, 9)
        foreach (var p in new[] { 0, 1, 2, 3, 8, 9 })
        {
            picker.MarkBlockReceived(p, 0, 16384);
            picker.MarkPieceVerified(p);
        }

        // Pick next blocks: should sequentially pick 4, 5, 6, 7
        var requests = picker.PickBlocks(fullBitfield, 4, sequentialMode: true);

        requests.Should().HaveCount(4);
        requests[0].PieceIndex.Should().Be(4);
        requests[1].PieceIndex.Should().Be(5);
        requests[2].PieceIndex.Should().Be(6);
        requests[3].PieceIndex.Should().Be(7);
    }

    [Test]
    public void PickBlocks_SequentialMode_HandlesSmallTorrentsUnder6Pieces()
    {
        // 4 pieces
        var picker = new PiecePicker(4, 16384, 65536);
        var fullBitfield = Enumerable.Repeat(true, 4).ToArray();

        var requests = picker.PickBlocks(fullBitfield, 4, sequentialMode: true);

        requests.Should().HaveCount(4);
        var pieces = requests.Select(r => r.PieceIndex).Distinct().ToList();
        pieces.Should().ContainInOrder(0, 1, 2, 3);
    }

    [Test]
    public void PickBlocks_SequentialMode_WithPartialPeerBitfield_PicksAvailableHeadOrTail()
    {
        var picker = new PiecePicker(20, 16384, 327680);

        // Peer only has tail piece 19 and interior piece 10
        var partialBitfield = new bool[20];
        partialBitfield[10] = true;
        partialBitfield[19] = true;

        var requests = picker.PickBlocks(partialBitfield, 2, sequentialMode: true);

        requests.Should().HaveCount(2);
        // Tail piece (19) prioritized before interior piece (10)
        requests[0].PieceIndex.Should().Be(19);
        requests[1].PieceIndex.Should().Be(10);
    }

    #endregion

    #region Endgame Mode Tests

    [Test]
    public void IsEndgameMode_ReturnsFalse_WhenManyBlocksRemaining()
    {
        // 100 pieces of 16KB = 100 blocks
        var picker = new PiecePicker(100, 16384, 1638400);

        picker.IsEndgameMode().Should().BeFalse();
    }

    [Test]
    public void IsEndgameMode_ReturnsFalse_OnCreationForSmallTorrent()
    {
        // 2 pieces of 16KB = 2 blocks (<= 30 blocks total / <= 480KB)
        var picker = new PiecePicker(2, 16384, 32768);
        picker.IsEndgameMode().Should().BeFalse();

        // Boundary test: exactly 30 blocks total does not enter endgame mode on creation
        var picker30 = new PiecePicker(30, 16384, 491520);
        picker30.IsEndgameMode().Should().BeFalse();
    }

    [Test]
    public void IsEndgameMode_ReturnsTrue_WhenRemainingBlocksUnderThreshold()
    {
        // 50 pieces of 16KB = 50 blocks (> 30 initially), 30 completed so 20 remaining (<= 30)
        var picker = new PiecePicker(50, 16384, 819200);

        for (var i = 0; i < 30; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeTrue();
    }

    [Test]
    public void IsEndgameMode_ReturnsFalse_WhenAllBlocksCompleted()
    {
        // 5 pieces of 16KB
        var picker = new PiecePicker(5, 16384, 81920);

        for (var i = 0; i < 5; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeFalse();
        picker.GetProgress().Should().Be(1.0);
    }

    [Test]
    public void PickBlocks_InNormalMode_PreventsDuplicateInFlightRequestsAcrossPeers()
    {
        // 50 pieces of 16KB (50 blocks > 30, so normal mode)
        var picker = new PiecePicker(50, 16384, 819200);
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        // Peer A requests 2 blocks
        var requestsPeerA = picker.PickBlocks(fullBitfield, 2);
        requestsPeerA.Should().HaveCount(2);

        // Peer B requests 2 blocks: should NOT get the blocks already in flight for Peer A
        var requestsPeerB = picker.PickBlocks(fullBitfield, 2);
        requestsPeerB.Should().HaveCount(2);

        var pieceA = requestsPeerA.Select(r => $"{r.PieceIndex}:{r.BlockOffset}").ToList();
        var pieceB = requestsPeerB.Select(r => $"{r.PieceIndex}:{r.BlockOffset}").ToList();

        pieceA.Intersect(pieceB).Should().BeEmpty();
    }

    [Test]
    public void PickBlocks_SmallPayload_DoesNotIssueDuplicateInFlightRequestsAcrossPeers()
    {
        // 2 pieces of 16KB = 2 blocks total
        var picker = new PiecePicker(2, 16384, 32768);
        var fullBitfield = new[] { true, true };

        picker.IsEndgameMode().Should().BeFalse();

        // Peer 1 requests 1 block
        var requestsPeer1 = picker.PickBlocks(fullBitfield, 1);
        requestsPeer1.Should().HaveCount(1);
        requestsPeer1[0].PieceIndex.Should().Be(0);

        // Peer 2 requests 2 blocks: should only receive piece 1, not duplicate in-flight piece 0
        var requestsPeer2 = picker.PickBlocks(fullBitfield, 2);
        requestsPeer2.Should().HaveCount(1);
        requestsPeer2[0].PieceIndex.Should().Be(1);

        // Piece 0 was not duplicated to Peer 2
        requestsPeer2.Select(r => r.PieceIndex).Should().NotContain(requestsPeer1[0].PieceIndex);
    }

    [Test]
    public async Task PickBlocks_TwoBlockTorrent_DoesNotIssueDuplicateInFlightRequestsAcrossPeersUntilTimeoutOrComplete()
    {
        // 2 pieces of 16KB = 2 blocks total
        var picker = new PiecePicker(2, 16384, 32768, requestTimeout: TimeSpan.FromMilliseconds(50));
        var fullBitfield = new[] { true, true };

        picker.IsEndgameMode().Should().BeFalse();

        // Peer 1 requests 1 block -> gets piece 0
        var requestsPeer1 = picker.PickBlocks(fullBitfield, 1);
        requestsPeer1.Should().HaveCount(1);
        requestsPeer1[0].PieceIndex.Should().Be(0);

        // Torrent is still not in endgame mode (1 in-flight < 2 remaining)
        picker.IsEndgameMode().Should().BeFalse();

        // Peer 2 requests 2 blocks -> should not receive piece 0 because it is in flight and not timed out; receives piece 1 only
        var requestsPeer2 = picker.PickBlocks(fullBitfield, 2);
        requestsPeer2.Should().HaveCount(1);
        requestsPeer2[0].PieceIndex.Should().Be(1);

        // No duplicate in-flight requests were issued across the two peers
        requestsPeer1[0].PieceIndex.Should().NotBe(requestsPeer2[0].PieceIndex);

        // Now both piece 0 and piece 1 are in-flight.
        // Complete piece 0.
        picker.MarkBlockReceived(0, 0, 16384);
        picker.MarkPieceVerified(0);

        // Piece 0 is complete, piece 1 is in-flight.
        // Wait for piece 1 to time out.
        await Task.Delay(75);

        // Now that piece 1 timed out, calling PickBlocks can re-request piece 1.
        var retryRequests = picker.PickBlocks(fullBitfield, 1);
        retryRequests.Should().HaveCount(1);
        retryRequests[0].PieceIndex.Should().Be(1);

        // Complete piece 1 as well.
        picker.MarkBlockReceived(1, 0, 16384);
        picker.MarkPieceVerified(1);

        // All pieces complete, no further requests issued and endgame mode is false.
        picker.PickBlocks(fullBitfield, 1).Should().BeEmpty();
        picker.IsEndgameMode().Should().BeFalse();
    }

    [Test]
    public void PickBlocks_InEndgameMode_AllowsDuplicateRequestsToFinishRapidly()
    {
        // 2 pieces of 16KB = 2 blocks
        var picker = new PiecePicker(2, 16384, 32768);
        var fullBitfield = new[] { true, true };

        // Before any requests, endgame mode is false
        picker.IsEndgameMode().Should().BeFalse();

        // Peer A requests all blocks so they are in flight
        var requestsPeerA = picker.PickBlocks(fullBitfield, 2);
        requestsPeerA.Should().HaveCount(2);

        // Once all remaining blocks are in flight, endgame mode is entered
        picker.IsEndgameMode().Should().BeTrue();

        // In endgame mode, Peer B can also request the in-flight blocks!
        var requestsPeerB = picker.PickBlocks(fullBitfield, 2);
        requestsPeerB.Should().HaveCount(2);

        requestsPeerB[0].PieceIndex.Should().Be(requestsPeerA[0].PieceIndex);
        requestsPeerB[1].PieceIndex.Should().Be(requestsPeerA[1].PieceIndex);
    }

    #endregion

    #region Choke, Cancellation & Corruption Recovery Tests

    [Test]
    public void CancelBlock_FreesInFlightBlockForOtherPeers()
    {
        // 50 pieces (normal mode)
        var picker = new PiecePicker(50, 16384, 819200);
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        var request1 = picker.PickBlocks(fullBitfield, 1);
        request1.Should().HaveCount(1);
        var pIdx = request1[0].PieceIndex;
        var offset = request1[0].BlockOffset;

        // Block is in flight, cannot be picked again
        var nextReq = picker.PickBlocks(fullBitfield, 1);
        nextReq[0].PieceIndex.Should().NotBe(pIdx);

        // Cancel the in-flight request (e.g. peer choked us)
        picker.CancelBlock(pIdx, offset);

        // Now block can be re-picked
        var retryReq = picker.PickBlocks(fullBitfield, 1);
        retryReq.Should().HaveCount(1);
        retryReq[0].PieceIndex.Should().Be(pIdx);
    }

    [Test]
    public void MarkBlockReceived_MultiBlockPiece_CompletesOnlyWhenAllBlocksArrive()
    {
        // 1 piece with 4 blocks of 16KB = 64KB
        var picker = new PiecePicker(1, 65536, 65536);

        // Block 0
        picker.MarkBlockReceived(0, 0, 16384).Should().BeFalse();
        // Block 1
        picker.MarkBlockReceived(0, 16384, 16384).Should().BeFalse();
        // Block 2
        picker.MarkBlockReceived(0, 32768, 16384).Should().BeFalse();
        // Block 3 (final)
        picker.MarkBlockReceived(0, 49152, 16384).Should().BeTrue();

        picker.MarkPieceVerified(0);
        picker.GetBitfield()[0].Should().BeTrue();
        picker.GetProgress().Should().Be(1.0);
    }

    [Test]
    public void MarkBlockReceived_DuplicateArrival_IgnoresDuplicateBlock()
    {
        var picker = new PiecePicker(1, 32768, 32768);

        picker.MarkBlockReceived(0, 0, 16384).Should().BeFalse();
        // Duplicate block 0 arrival
        picker.MarkBlockReceived(0, 0, 16384).Should().BeFalse();

        // Receive block 1
        picker.MarkBlockReceived(0, 16384, 16384).Should().BeTrue();
    }

    [Test]
    public void MarkPieceCorrupt_ResetsPieceStateAndAllowsFullRedownload()
    {
        // 1 piece with 2 blocks
        var picker = new PiecePicker(1, 32768, 32768);
        var fullBitfield = new[] { true };

        picker.MarkBlockReceived(0, 0, 16384);
        picker.MarkBlockReceived(0, 16384, 16384);
        picker.MarkPieceVerified(0);

        picker.GetBitfield()[0].Should().BeTrue();
        picker.GetProgress().Should().Be(1.0);

        // Verification failed on hash check: mark corrupt
        picker.MarkPieceCorrupt(0);

        picker.GetBitfield()[0].Should().BeFalse();
        picker.GetProgress().Should().Be(0.0);

        // Piece should be pickable again
        var retryRequests = picker.PickBlocks(fullBitfield, 2);
        retryRequests.Should().HaveCount(2);
        retryRequests[0].PieceIndex.Should().Be(0);
        retryRequests[1].PieceIndex.Should().Be(0);
    }

    [Test]
    public void MarkBlockReceived_WithInvalidPieceOrOffset_ReturnsFalseWithoutThrowing()
    {
        var picker = new PiecePicker(2, 16384, 32768);

        picker.MarkBlockReceived(-1, 0, 16384).Should().BeFalse();
        picker.MarkBlockReceived(99, 0, 16384).Should().BeFalse();
        picker.MarkBlockReceived(0, 999999, 16384).Should().BeFalse();
    }

    [Test]
    public void SetPiecePriority_WithInvalidPiece_DoesNotThrow()
    {
        var picker = new PiecePicker(2, 16384, 32768);

        Action act1 = () => picker.SetPiecePriority(-1, 2);
        act1.Should().NotThrow();

        Action act2 = () => picker.SetPiecePriority(100, 2);
        act2.Should().NotThrow();
    }

    [Test]
    public void MarkPieceVerifiedAndCorrupt_WithInvalidPiece_DoesNotThrow()
    {
        var picker = new PiecePicker(2, 16384, 32768);

        Action act1 = () => picker.MarkPieceVerified(-1);
        act1.Should().NotThrow();

        Action act2 = () => picker.MarkPieceVerified(100);
        act2.Should().NotThrow();

        Action act3 = () => picker.MarkPieceCorrupt(-1);
        act3.Should().NotThrow();

        Action act4 = () => picker.MarkPieceCorrupt(100);
        act4.Should().NotThrow();
    }

    #endregion

    #region In-Flight Request Timeout & Malformed Bitfield Tests

    [Test]
    public async Task PickBlocks_WhenBlockRequestTimesOut_AllowsReassigningBlockToAnotherPeer()
    {
        // 50 pieces, 16KB each, with a short timeout of 50ms
        var picker = new PiecePicker(50, 16384, 819200, requestTimeout: TimeSpan.FromMilliseconds(50));
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        // Peer A requests 1 block
        var requestA = picker.PickBlocks(fullBitfield, 1);
        requestA.Should().HaveCount(1);
        var pIdx = requestA[0].PieceIndex;

        // Immediately, Peer B attempts to pick: block is in flight, so it skips block pIdx
        var nextReq = picker.PickBlocks(fullBitfield, 1);
        nextReq.Should().HaveCount(1);
        nextReq[0].PieceIndex.Should().NotBe(pIdx);

        // Wait for request to time out
        await Task.Delay(75);

        // After timeout, block pIdx can be re-picked by Peer B
        var retryReq = picker.PickBlocks(fullBitfield, 1);
        retryReq.Should().HaveCount(1);
        retryReq[0].PieceIndex.Should().Be(pIdx);
    }

    [Test]
    public async Task PruneTimedOutRequests_RemovesExpiredEntries()
    {
        var picker = new PiecePicker(50, 16384, 819200, requestTimeout: TimeSpan.FromMilliseconds(50));
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        picker.PickBlocks(fullBitfield, 3);
        picker.InFlightBlockCount.Should().Be(3);

        // Immediately call prune: none should be expired yet
        picker.PruneTimedOutRequests().Should().Be(0);
        picker.InFlightBlockCount.Should().Be(3);

        // Wait for timeout to expire
        await Task.Delay(75);

        // Call prune: all 3 entries should be removed
        picker.PruneTimedOutRequests().Should().Be(3);
        picker.InFlightBlockCount.Should().Be(0);
    }

    [Test]
    public void UpdatePeerAvailability_WithInvalidBitfieldLengthOrSpareBits_SafelyIgnored()
    {
        var picker = new PiecePicker(10, 16384, 163840);

        // 1. Shorter than piece count (5 < 10)
        picker.UpdatePeerAvailability(new bool[5], isAdd: true);
        picker.GetAvailability().All(a => a == 0).Should().BeTrue();

        // 2. Excessively long (100 > 16)
        picker.UpdatePeerAvailability(new bool[100], isAdd: true);
        picker.GetAvailability().All(a => a == 0).Should().BeTrue();

        // 3. Byte-aligned length 16, but spare bit 10 is set to true
        var malformedWithSpareBit = new bool[16];
        malformedWithSpareBit[10] = true;
        picker.UpdatePeerAvailability(malformedWithSpareBit, isAdd: true);
        picker.GetAvailability().All(a => a == 0).Should().BeTrue();

        // 4. Valid bitfield updates availability
        var validBitfield = new bool[10];
        validBitfield[0] = true;
        picker.UpdatePeerAvailability(validBitfield, isAdd: true);
        picker.GetAvailability()[0].Should().Be(1);
    }

    [Test]
    public void PickBlocks_WithInvalidBitfieldLengthOrSpareBits_ReturnsEmptyList()
    {
        var picker = new PiecePicker(10, 16384, 163840);

        // 1. Null
        picker.PickBlocks(null!, 5).Should().BeEmpty();

        // 2. Shorter than piece count
        picker.PickBlocks(new bool[5], 5).Should().BeEmpty();

        // 3. Excessively long
        picker.PickBlocks(new bool[100], 5).Should().BeEmpty();

        // 4. Spare bit set
        var malformedWithSpareBit = new bool[16];
        malformedWithSpareBit[0] = true;
        malformedWithSpareBit[12] = true;
        picker.PickBlocks(malformedWithSpareBit, 5).Should().BeEmpty();
    }

    [Test]
    public void Constructor_WithNegativePieceCount_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => new PiecePicker(-1, 16384, 163840);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Constructor_WithZeroPieceCount_ThrowsArgumentOutOfRangeException()
    {
        Action act = () => new PiecePicker(0, 16384, 163840);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Constructor_WithZeroOrNegativePieceLength_ThrowsArgumentOutOfRangeException()
    {
        Action act1 = () => new PiecePicker(10, 0, 163840);
        act1.Should().Throw<ArgumentOutOfRangeException>();

        Action act2 = () => new PiecePicker(10, -16384, 163840);
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void Constructor_WhenTotalSizeProducesNegativeFinalPieceLength_ThrowsArgumentException()
    {
        // 5 pieces of 16384 = at least 65537 bytes needed for 5 pieces.
        // Total size 1000 means piece 4 len is 1000 - 4 * 16384 = -64536 < 0.
        Action act = () => new PiecePicker(5, 16384, 1000);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*must be positive*");
    }

    [Test]
    public void SetAvailability_SetsSwarmAvailabilityCorrectly()
    {
        var picker = new PiecePicker(5, 16384, 81920);

        var customAvailability = new[] { 3, 1, 4, 1, 5 };
        picker.SetAvailability(customAvailability);

        picker.GetAvailability().Should().Equal(customAvailability);

        // Verify rarest-first selection uses the set availability
        var fullBitfield = Enumerable.Repeat(true, 5).ToArray();
        var requests = picker.PickBlocks(fullBitfield, 1);
        requests.Should().HaveCount(1);
        // Piece 1 or 3 has rarity 1 (rarest)
        requests[0].PieceIndex.Should().BeOneOf(1, 3);
    }

    [Test]
    public void UpdateAvailability_SetsSwarmAvailabilityCorrectly()
    {
        var picker = new PiecePicker(5, 16384, 81920);

        picker.UpdateAvailability(new[] { 0, 5, 2, 8, 1 });

        picker.GetAvailability().Should().Equal(new[] { 0, 5, 2, 8, 1 });
    }

    [Test]
    public void SetAvailability_HandlesNullOrDifferentLengthArrays()
    {
        var picker = new PiecePicker(5, 16384, 81920);

        // Null should not throw or change anything
        picker.SetAvailability(null!);
        picker.GetAvailability().Should().Equal(new[] { 0, 0, 0, 0, 0 });

        // Shorter array sets available elements and zeroes remaining
        picker.SetAvailability(new[] { 2, 4 });
        picker.GetAvailability().Should().Equal(new[] { 2, 4, 0, 0, 0 });

        // Longer array truncates to pieceCount
        picker.SetAvailability(new[] { 1, 2, 3, 4, 5, 6, 7 });
        picker.GetAvailability().Should().Equal(new[] { 1, 2, 3, 4, 5 });
    }

    #endregion
}
