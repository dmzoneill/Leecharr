// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BitTorrentPiecePickerAndSwarmComprehensiveIntegrationTests : IntegrationTestBase
{
    private const int PieceLength = 32 * 1024; // 2 blocks per piece
    private const int PieceCount = 10;
    private const long TotalSize = PieceCount * PieceLength;

    [Test]
    public void PiecePicker_Constructor_ValidatesParametersAndComputesBlocks()
    {
        // 1. Valid initialization
        var picker = new PiecePicker(PieceCount, PieceLength, TotalSize);
        picker.PieceCount.Should().Be(PieceCount);
        picker.PieceLength.Should().Be(PieceLength);
        picker.TotalSize.Should().Be(TotalSize);

        // 2. Invalid inputs
        Action actZeroCount = () => new PiecePicker(0, PieceLength, TotalSize);
        actZeroCount.Should().Throw<ArgumentOutOfRangeException>();

        Action actZeroLength = () => new PiecePicker(PieceCount, 0, TotalSize);
        actZeroLength.Should().Throw<ArgumentOutOfRangeException>();

        Action actNegativeSize = () => new PiecePicker(PieceCount, PieceLength, -1);
        actNegativeSize.Should().Throw<ArgumentException>();
    }

    [Test]
    public void PiecePicker_PickBlocks_SequentialAndAvailabilityPrioritized()
    {
        var picker = new PiecePicker(PieceCount, PieceLength, TotalSize);
        var bitfield = new bool[PieceCount];
        Array.Fill(bitfield, true);

        // 1. Sequential picking picks piece 0 blocks first
        var seqRequests = picker.PickBlocks(bitfield, 2, sequentialMode: true, peerId: "peer-1");
        seqRequests.Should().HaveCount(2);
        seqRequests[0].PieceIndex.Should().Be(0);
        seqRequests[0].BlockOffset.Should().Be(0);
        seqRequests[1].PieceIndex.Should().Be(0);
        seqRequests[1].BlockOffset.Should().Be(PiecePicker.DefaultBlockSize);

        // 2. Swarm availability updates
        var rareBitfield = new bool[PieceCount];
        rareBitfield[5] = true;
        rareBitfield[6] = true;
        picker.UpdatePeerAvailability(rareBitfield, isAdd: true);

        // Rarest pieces (low availability) or sequential mode
        picker.GetPiecePriority(0).Should().Be(1);
    }

    [Test]
    public void PiecePicker_BlockLifecycle_ReceiveVerifyAndReset()
    {
        var picker = new PiecePicker(PieceCount, PieceLength, TotalSize);
        var bitfield = new bool[PieceCount];
        Array.Fill(bitfield, true);

        // 1. Request block
        var requests = picker.PickBlocks(bitfield, 1, sequentialMode: true, peerId: "peer-A");
        requests.Should().HaveCount(1);
        picker.InFlightBlockCount.Should().Be(1);

        // 2. Receive block 0
        var isComplete = picker.MarkBlockReceived(0, 0, PiecePicker.DefaultBlockSize);
        isComplete.Should().BeFalse(); // Only 1 of 2 blocks received for piece 0

        // 3. Receive block 1
        var isPieceDone = picker.MarkBlockReceived(0, PiecePicker.DefaultBlockSize, PiecePicker.DefaultBlockSize, "peer-B", out _);
        isPieceDone.Should().BeTrue(); // Both blocks received

        var contributors = picker.GetContributingPeers(0);
        contributors.Should().Contain("peer-B");

        // 4. Mark verified
        picker.MarkPieceVerified(0);
        picker.GetProgress().Should().BeGreaterThan(0);

        // 5. Mark piece corrupt / reset
        var previousContributors = picker.MarkPieceCorrupt(0);
        previousContributors.Should().NotBeNull();
    }

    [Test]
    public void PiecePicker_PrioritiesAndCancellations_ManagesRequests()
    {
        var picker = new PiecePicker(PieceCount, PieceLength, TotalSize);
        var bitfield = new bool[PieceCount];
        Array.Fill(bitfield, true);

        // 1. Skip priority (0)
        picker.SetPiecePriority(0, 0);
        picker.GetPiecePriority(0).Should().Be(0);

        var requests = picker.PickBlocks(bitfield, 1, sequentialMode: true, peerId: "peer-X");
        requests.Should().HaveCount(1);
        requests[0].PieceIndex.Should().NotBe(0); // Piece 0 skipped

        // 2. Cancel block and verify event
        var cancelled = false;
        picker.BlockCancelled += args =>
        {
            if (args.PieceIndex == requests[0].PieceIndex)
            {
                cancelled = true;
            }
        };

        picker.CancelBlock(requests[0].PieceIndex, requests[0].BlockOffset, requests[0].BlockLength, "peer-X");
        cancelled.Should().BeTrue();

        // 3. Snubbed peer limited to 1 request
        var snubbedReqs = picker.PickBlocks(bitfield, 5, sequentialMode: false, peerId: "peer-snubbed", isSnubbed: true);
        snubbedReqs.Count.Should().BeLessThanOrEqualTo(1);

        // 4. Timeout property configuration
        picker.RequestTimeout = TimeSpan.FromSeconds(30);
        picker.RequestTimeout.TotalSeconds.Should().Be(30);
    }
}
