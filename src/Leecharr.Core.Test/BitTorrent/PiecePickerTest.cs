// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

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
        // 4 pieces: head [0], tail [2, 3], rest [1] -> [0, 2, 3, 1]
        var picker4 = new PiecePicker(4, 16384, 65536);
        var fullBitfield4 = Enumerable.Repeat(true, 4).ToArray();
        var requests4 = picker4.PickBlocks(fullBitfield4, 4, sequentialMode: true);
        var pieces4 = requests4.Select(r => r.PieceIndex).Distinct().ToList();
        pieces4.Should().ContainInOrder(0, 2, 3, 1);

        // 5 pieces: head [0, 1], tail [3, 4], rest [2] -> [0, 1, 3, 4, 2]
        var picker5 = new PiecePicker(5, 16384, 81920);
        var fullBitfield5 = Enumerable.Repeat(true, 5).ToArray();
        var requests5 = picker5.PickBlocks(fullBitfield5, 5, sequentialMode: true);
        var pieces5 = requests5.Select(r => r.PieceIndex).Distinct().ToList();
        pieces5.Should().ContainInOrder(0, 1, 3, 4, 2);

        // 6 pieces: head [0, 1], tail [4, 5], rest [2, 3] -> [0, 1, 4, 5, 2, 3]
        var picker6 = new PiecePicker(6, 16384, 98304);
        var fullBitfield6 = Enumerable.Repeat(true, 6).ToArray();
        var requests6 = picker6.PickBlocks(fullBitfield6, 6, sequentialMode: true);
        var pieces6 = requests6.Select(r => r.PieceIndex).Distinct().ToList();
        pieces6.Should().ContainInOrder(0, 1, 4, 5, 2, 3);
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
        // 2 pieces of 16KB = 2 blocks (<= 20 pieces total)
        var picker = new PiecePicker(2, 16384, 32768);
        picker.IsEndgameMode().Should().BeFalse();

        // Boundary test: exactly 20 pieces total does not enter endgame mode on creation
        var picker20 = new PiecePicker(20, 16384, 327680);
        picker20.IsEndgameMode().Should().BeFalse();
    }

    [Test]
    public void IsEndgameMode_ReturnsTrue_WhenRemainingPiecesUnderTwenty()
    {
        // 50 pieces of 16KB = 50 blocks (> 20 initially), 30 completed so 20 remaining (<= 20)
        var picker = new PiecePicker(50, 16384, 819200);

        for (var i = 0; i < 30; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeTrue();
    }

    [Test]
    public void IsEndgameMode_ReturnsTrue_ForLargePieceSize_WhenRemainingPiecesUnderTwenty()
    {
        // 50 pieces of 4MB each = 200MB (each piece has 256 blocks of 16KB)
        var picker = new PiecePicker(50, 4194304, 209715200L);

        // Complete 31 pieces -> 19 remaining (<= 20)
        for (var i = 0; i < 31; i++)
        {
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeTrue();
    }

    [Test]
    public void IsEndgameMode_ReturnsTrue_WhenRemainingBytesUnderTwoPercent()
    {
        // 1000 pieces of 16KB = 16,384,000 bytes
        // 985 pieces completed -> 15 pieces remaining = 1.5% < 2%
        var picker = new PiecePicker(1000, 16384, 16384000);

        for (var i = 0; i < 985; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeTrue();
    }

    [Test]
    public void IsEndgameMode_ReturnsFalse_WhenEndGamePickerDisabledInConfig()
    {
        var configService = Substitute.For<IConfigService>();
        configService.EndGamePickerEnabled.Returns(false);

        var picker = new PiecePicker(50, 16384, 819200, configService: configService);
        for (var i = 0; i < 30; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeFalse();
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
        // 50 pieces of 16KB (50 blocks > 20, so normal mode)
        var picker = new PiecePicker(50, 16384, 819200);
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        // Peer A requests 2 blocks
        var requestsPeerA = picker.PickBlocks(fullBitfield, 2, peerId: "peerA");
        requestsPeerA.Should().HaveCount(2);

        // Peer B requests 2 blocks: should NOT get the blocks already in flight for Peer A
        var requestsPeerB = picker.PickBlocks(fullBitfield, 2, peerId: "peerB");
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
        var requestsPeer1 = picker.PickBlocks(fullBitfield, 1, peerId: "peer1");
        requestsPeer1.Should().HaveCount(1);
        requestsPeer1[0].PieceIndex.Should().Be(0);

        // Peer 2 requests 2 blocks: should only receive piece 1, not duplicate in-flight piece 0
        var requestsPeer2 = picker.PickBlocks(fullBitfield, 2, peerId: "peer2");
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
        var requestsPeer1 = picker.PickBlocks(fullBitfield, 1, peerId: "peer1");
        requestsPeer1.Should().HaveCount(1);
        requestsPeer1[0].PieceIndex.Should().Be(0);

        // Torrent is still not in endgame mode (1 in-flight < 2 remaining)
        picker.IsEndgameMode().Should().BeFalse();

        // Peer 2 requests 2 blocks -> should not receive piece 0 because it is in flight and not timed out; receives piece 1 only
        var requestsPeer2 = picker.PickBlocks(fullBitfield, 2, peerId: "peer2");
        requestsPeer2.Should().HaveCount(1);
        requestsPeer2[0].PieceIndex.Should().Be(1);

        // No duplicate in-flight requests were issued across the two peers
        requestsPeer1[0].PieceIndex.Should().NotBe(requestsPeer2[0].PieceIndex);

        // Now both piece 0 and piece 1 are in-flight.
        // Complete piece 0.
        picker.MarkBlockReceived(0, 0, 16384, "peer1", out _);
        picker.MarkPieceVerified(0);

        // Piece 0 is complete, piece 1 is in-flight.
        // Wait for piece 1 to time out.
        await Task.Delay(75);

        // Now that piece 1 timed out, calling PickBlocks can re-request piece 1.
        var retryRequests = picker.PickBlocks(fullBitfield, 1, peerId: "peer3");
        retryRequests.Should().HaveCount(1);
        retryRequests[0].PieceIndex.Should().Be(1);

        // Complete piece 1 as well.
        picker.MarkBlockReceived(1, 0, 16384, "peer3", out _);
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
        var requestsPeerA = picker.PickBlocks(fullBitfield, 2, peerId: "peerA");
        requestsPeerA.Should().HaveCount(2);

        // Once all remaining blocks are in flight, endgame mode is entered
        picker.IsEndgameMode().Should().BeTrue();

        // In endgame mode, Peer B can also request the in-flight blocks!
        var requestsPeerB = picker.PickBlocks(fullBitfield, 2, peerId: "peerB");
        requestsPeerB.Should().HaveCount(2);

        requestsPeerB[0].PieceIndex.Should().Be(requestsPeerA[0].PieceIndex);
        requestsPeerB[1].PieceIndex.Should().Be(requestsPeerA[1].PieceIndex);
    }

    [Test]
    public void MarkBlockReceived_InEndgameMode_TriggersCancellationForOtherDuplicatePeers()
    {
        // 2 pieces of 16KB = 2 blocks
        var picker = new PiecePicker(2, 16384, 32768);
        var fullBitfield = new[] { true, true };

        // Peer A requests both blocks
        picker.PickBlocks(fullBitfield, 2, peerId: "peerA");
        picker.IsEndgameMode().Should().BeTrue();

        // Peer B duplicates block requests in endgame mode
        picker.PickBlocks(fullBitfield, 2, peerId: "peerB");

        BlockCancelledEventArgs cancelledEvent = null;
        picker.BlockCancelled += e => cancelledEvent = e;

        // Block 0 arrives from peerA
        var pieceComplete = picker.MarkBlockReceived(0, 0, 16384, "peerA", out var cancelledPeers);

        // Peer B should be notified of cancellation for block 0
        cancelledPeers.Should().ContainSingle().Which.Should().Be("peerB");
        cancelledEvent.Should().NotBeNull();
        cancelledEvent.PieceIndex.Should().Be(0);
        cancelledEvent.BlockOffset.Should().Be(0);
        cancelledEvent.CancelledPeerIds.Should().Contain("peerB");
    }

    [Test]
    public void PickBlocks_WithSnubbedPeer_LimitsRequestsToSingleBlockProbe()
    {
        // 50 pieces in normal mode
        var picker = new PiecePicker(50, 16384, 819200);
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        // Non-snubbed peer requests 5 blocks
        var activeRequests = picker.PickBlocks(fullBitfield, 5, peerId: "activePeer", isSnubbed: false);
        activeRequests.Should().HaveCount(5);

        // Snubbed peer requests 5 blocks -> throttled to at most 1 block probe
        var snubbedRequests = picker.PickBlocks(fullBitfield, 5, peerId: "snubbedPeer", isSnubbed: true);
        snubbedRequests.Should().HaveCount(1);
    }

    [Test]
    public void CancelBlock_WithSpecificPeerId_OnlyCancelsForThatPeer()
    {
        var picker = new PiecePicker(2, 16384, 32768);
        var fullBitfield = new[] { true, true };

        picker.PickBlocks(fullBitfield, 2, peerId: "peerA");
        picker.PickBlocks(fullBitfield, 2, peerId: "peerB");

        picker.GetDuplicateInFlightPeers(0, 0).Should().Contain(new[] { "peerA", "peerB" });

        // Cancel only for peerA
        picker.CancelBlock(0, 0, peerId: "peerA");

        var remainingPeers = picker.GetDuplicateInFlightPeers(0, 0);
        remainingPeers.Should().ContainSingle().Which.Should().Be("peerB");
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
    public async Task PickBlocks_WhenBlockRequestTimesOutAndIsReRequested_RefreshesTimestampAndDoesNotReRequestToSubsequentPeersUntilNewTimeoutExpires()
    {
        // 2 pieces, 16KB each (1 block per piece), requestTimeout: 50ms
        var picker = new PiecePicker(2, 16384, 32768, requestTimeout: TimeSpan.FromMilliseconds(50));
        var fullBitfield = new[] { true, true };

        // Peer 1 requests block 0:0
        var req1 = picker.PickBlocks(fullBitfield, 1, peerId: "peer-1");
        req1.Should().HaveCount(1);
        req1[0].PieceIndex.Should().Be(0);

        // Peer 2 requests block 1:0
        var req2 = picker.PickBlocks(fullBitfield, 1, peerId: "peer-2");
        req2.Should().HaveCount(1);
        req2[0].PieceIndex.Should().Be(1);

        // Peer 3 immediately attempts to pick: both blocks in-flight, returns empty
        var req3 = picker.PickBlocks(fullBitfield, 1, peerId: "peer-3");
        req3.Should().BeEmpty();

        // Wait for requests to time out
        await Task.Delay(75);

        // Peer 3 now picks the timed-out block 0:0
        var retryPeer3 = picker.PickBlocks(fullBitfield, 1, peerId: "peer-3");
        retryPeer3.Should().HaveCount(1);
        retryPeer3[0].PieceIndex.Should().Be(0);

        // Immediately (before new timeout), Peer 4 picks: block 0:0 timestamp is refreshed to now,
        // so Peer 4 does NOT get block 0:0; it gets timed-out block 1:0 instead
        var retryPeer4 = picker.PickBlocks(fullBitfield, 1, peerId: "peer-4");
        retryPeer4.Should().HaveCount(1);
        retryPeer4[0].PieceIndex.Should().Be(1);

        // Immediately, Peer 5 attempts to pick: both blocks have had their timestamps refreshed,
        // so no more blocks are available or timed out
        var retryPeer5 = picker.PickBlocks(fullBitfield, 1, peerId: "peer-5");
        retryPeer5.Should().BeEmpty();

        // Check that peer-1 was cleared from block 0:0 peer requests and replaced by peer-3
        var inFlightPeersPiece0 = picker.GetDuplicateInFlightPeers(0, 0, excludingPeerId: null);
        inFlightPeersPiece0.Should().ContainSingle().Which.Should().Be("peer-3");

        // Check that peer-2 was cleared from block 1:0 peer requests and replaced by peer-4
        var inFlightPeersPiece1 = picker.GetDuplicateInFlightPeers(1, 0, excludingPeerId: null);
        inFlightPeersPiece1.Should().ContainSingle().Which.Should().Be("peer-4");
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

    [Test]
    public void GetProgress_SelectiveDownloading_ReturnsAccurateProgressForActivePieces()
    {
        // 1000 pieces total, 900 unselected (Priority = 0), 100 selected (Priority = 1)
        var picker = new PiecePicker(1000, 16384, 16384000);

        for (int i = 100; i < 1000; i++)
        {
            picker.SetPiecePriority(i, 0);
        }

        // Initially 0% progress
        picker.GetProgress().Should().Be(0.0);

        // Verify 50 of the 100 active pieces
        for (int i = 0; i < 50; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.GetProgress().Should().Be(0.5);

        // Verify the remaining 50 active pieces
        for (int i = 50; i < 100; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.GetProgress().Should().Be(1.0);
    }

    [Test]
    public void GetProgress_WhenAllPiecesUnselected_ReturnsZero()
    {
        var picker = new PiecePicker(10, 16384, 163840);
        for (int i = 0; i < 10; i++)
        {
            picker.SetPiecePriority(i, 0);
        }

        picker.GetProgress().Should().Be(0.0);
    }

    #endregion

    #region BEP 6 Fast Extension RejectRequest Tests

    [Test]
    public void RejectRequest_ImmediatelyEvictsInFlightBlock_WithoutWaitingForTimeout()
    {
        // 50 pieces, 16KB each, long timeout of 30 seconds
        var picker = new PiecePicker(50, 16384, 819200, requestTimeout: TimeSpan.FromSeconds(30));
        var fullBitfield = Enumerable.Repeat(true, 50).ToArray();

        // Peer A requests 1 block -> piece 0 block 0
        var requestA = picker.PickBlocks(fullBitfield, 1, peerId: "peerA");
        requestA.Should().HaveCount(1);
        picker.InFlightBlockCount.Should().Be(1);

        // Another peer attempts to pick: block is in flight, so it gets piece 1 block 0
        var nextReq = picker.PickBlocks(fullBitfield, 1, peerId: "peerB");
        nextReq.Should().HaveCount(1);
        nextReq[0].PieceIndex.Should().NotBe(requestA[0].PieceIndex);

        // Peer A rejects the request (BEP 6 Reject Request)
        picker.RejectRequest(requestA[0].PieceIndex, requestA[0].BlockOffset);

        // In-flight count decreases immediately without waiting 30 seconds
        picker.InFlightBlockCount.Should().Be(1); // Only peerB's block remains in flight

        // Now piece 0 block 0 is immediately pickable again by peer C
        var retryReq = picker.PickBlocks(fullBitfield, 1, peerId: "peerC");
        retryReq.Should().HaveCount(1);
        retryReq[0].PieceIndex.Should().Be(requestA[0].PieceIndex);
        retryReq[0].BlockOffset.Should().Be(requestA[0].BlockOffset);
    }

    [Test]
    public void RejectRequest_WithPeerId_EvictsInFlightBlockForThatSpecificPeer()
    {
        var picker = new PiecePicker(2, 16384, 32768);
        var fullBitfield = new[] { true, true };

        // Enter endgame mode so both peers can request block 0:0
        picker.PickBlocks(fullBitfield, 2, peerId: "peerA");
        picker.PickBlocks(fullBitfield, 2, peerId: "peerB");

        picker.GetDuplicateInFlightPeers(0, 0).Should().Contain(new[] { "peerA", "peerB" });

        // Peer A rejects the request
        picker.RejectRequest(0, 0, peerId: "peerA");

        // Peer A is removed from in-flight tracking for block 0:0, but peerB remains
        var remainingPeers = picker.GetDuplicateInFlightPeers(0, 0);
        remainingPeers.Should().ContainSingle().Which.Should().Be("peerB");
    }

    [Test]
    public void RejectRequest_WithLengthOverload_EvictsInFlightBlock()
    {
        var picker = new PiecePicker(10, 16384, 163840);
        var fullBitfield = Enumerable.Repeat(true, 10).ToArray();

        var requests = picker.PickBlocks(fullBitfield, 1, peerId: "peerA");
        requests.Should().HaveCount(1);
        picker.InFlightBlockCount.Should().Be(1);

        picker.RejectRequest(requests[0].PieceIndex, requests[0].BlockOffset, requests[0].BlockLength, "peerA");
        picker.InFlightBlockCount.Should().Be(0);

        var retry = picker.PickBlocks(fullBitfield, 1, peerId: "peerB");
        retry.Should().HaveCount(1);
        retry[0].PieceIndex.Should().Be(requests[0].PieceIndex);
    }

    #endregion

    #region Dynamic Thresholds & Config Binding Tests (Issue #575)

    [Test]
    public void RequestTimeout_BoundToConfigService_UsesConfiguredTimeout()
    {
        var configService = Substitute.For<IConfigService>();
        configService.StaleRequestTimeoutSeconds.Returns(45);

        var picker = new PiecePicker(10, 16384, 163840, configService: configService);
        picker.RequestTimeout.Should().Be(TimeSpan.FromSeconds(45));

        configService.StaleRequestTimeoutSeconds.Returns(15);
        picker.RequestTimeout.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Test]
    public void PickBlocks_SequentialMode_LargeMediaFile_DynamicallyPrioritizesMultiMegabyteHeadAndTail()
    {
        // 1000 pieces of 256KB = 256MB total (> 2MB)
        // targetHeadBytes: 8MB -> 32 pieces (0..31)
        // targetTailBytes: 4MB -> 16 pieces (984..999)
        var picker = new PiecePicker(1000, 262144, 268435456L);
        var fullBitfield = Enumerable.Repeat(true, 1000).ToArray();

        // Initially, requests come from head pieces (0..31)
        var headRequests = picker.PickBlocks(fullBitfield, 10, sequentialMode: true);
        headRequests.Should().HaveCount(10);
        headRequests.All(r => r.PieceIndex >= 0 && r.PieceIndex < 32).Should().BeTrue();

        // Mark head pieces (0..31) complete and verified
        for (var i = 0; i < 32; i++)
        {
            picker.MarkPieceVerified(i);
        }

        // Now, picking blocks should pick from tail pieces (984..999) before interior piece 32
        var tailRequests = picker.PickBlocks(fullBitfield, 16, sequentialMode: true);
        tailRequests.Should().HaveCount(16);
        tailRequests.All(r => r.PieceIndex >= 984 && r.PieceIndex < 1000).Should().BeTrue();

        // Mark tail pieces (984..999) complete and verified
        for (var i = 984; i < 1000; i++)
        {
            picker.MarkPieceVerified(i);
        }

        // Now, picking blocks should sequentially resume with interior piece 32
        var interiorRequests = picker.PickBlocks(fullBitfield, 1, sequentialMode: true);
        interiorRequests.Should().HaveCount(1);
        interiorRequests[0].PieceIndex.Should().Be(32);
    }

    [Test]
    public void IsEndgameMode_DynamicBlockPercentageScaling_TriggersAtTwoPercentRemaining()
    {
        // 500 pieces of 16KB = 8MB (500 blocks total)
        // 2% of 500 = 10 blocks
        // When 491 pieces are verified -> 9 remaining (1.8% < 2%) -> endgame mode triggers!
        var picker = new PiecePicker(500, 16384, 8192000);

        for (var i = 0; i < 491; i++)
        {
            picker.MarkBlockReceived(i, 0, 16384);
            picker.MarkPieceVerified(i);
        }

        picker.IsEndgameMode().Should().BeTrue();
    }

    #endregion
}
