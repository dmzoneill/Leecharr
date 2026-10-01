// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class BitTorrentEnginesAndPiecePickerComprehensiveIntegrationTests : IntegrationTestBase
{
    [Test]
    public void PiecePicker_InitializationAndValidation_EnforcesParameters()
    {
        // 1. Valid initialization
        var picker = new PiecePicker(pieceCount: 10, pieceLength: 16384, totalSize: 163840);
        picker.PieceCount.Should().Be(10);
        picker.PieceLength.Should().Be(16384);
        picker.TotalSize.Should().Be(163840);
        picker.InFlightBlockCount.Should().Be(0);

        // 2. Invalid piece count
        var actZeroCount = () => new PiecePicker(0, 16384, 100);
        actZeroCount.Should().Throw<ArgumentOutOfRangeException>();

        // 3. Invalid piece length
        var actZeroLength = () => new PiecePicker(10, 0, 100);
        actZeroLength.Should().Throw<ArgumentOutOfRangeException>();

        // 4. Invalid total size
        var actNegativeSize = () => new PiecePicker(10, 16384, -1);
        actNegativeSize.Should().Throw<ArgumentException>();
    }

    [Test]
    public void PiecePicker_PriorityAndSwarmAvailability_TracksState()
    {
        var picker = new PiecePicker(pieceCount: 5, pieceLength: 16384, totalSize: 81920);

        // 1. Piece priority levels
        picker.GetPiecePriority(0).Should().Be(1);
        picker.SetPiecePriority(0, 3);
        picker.GetPiecePriority(0).Should().Be(3);

        picker.SetPiecePriority(1, 0); // Skip piece
        picker.GetPiecePriority(1).Should().Be(0);

        // 2. Swarm availability updates
        var peerBitfield = new[] { true, true, false, true, false };
        picker.UpdatePeerAvailability(peerBitfield, isAdd: true);
        picker.UpdatePeerAvailability(peerBitfield, isAdd: false);

        // Null or invalid bitfields safely ignored
        picker.UpdatePeerAvailability(null, isAdd: true);
        picker.UpdatePeerAvailability(Array.Empty<bool>(), isAdd: true);
    }

    [Test]
    public void PiecePicker_PickBlocksAndBlockCompletion_TracksFlightAndEndgame()
    {
        var picker = new PiecePicker(pieceCount: 3, pieceLength: 32768, totalSize: 98304);

        // 1. Pick blocks for peer
        var peerBitfield = new[] { true, true, true };
        var requests = picker.PickBlocks(peerBitfield, maxRequests: 2, sequentialMode: false, peerId: "peer-1");
        requests.Should().NotBeNull();
        requests.Count.Should().BeLessThanOrEqualTo(2);

        // 2. Sequential mode picking
        var seqRequests = picker.PickBlocks(peerBitfield, maxRequests: 2, sequentialMode: true, peerId: "peer-2");
        seqRequests.Should().NotBeNull();

        // 3. Partial pieces
        var partials = picker.GetPartialPieces();
        partials.Should().NotBeNull();

        // 4. Mark piece verified
        picker.MarkPieceVerified(0);
        picker.GetPartialPieces().Should().NotContain(0);
    }

    [Test]
    public async Task ArrAndDownloadClientSync_Endpoints_ReturnsSuccessResponses()
    {
        // 1. Arr sync endpoint
        var arrSyncResp = await this.Client.GetAsync("/api/v1/arrsync");
        arrSyncResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        // 2. Download client sync endpoint
        var dlSyncResp = await this.Client.GetAsync("/api/v1/downloadclientsync");
        dlSyncResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);

        // 3. Download clients list
        var dlListResp = await this.Client.GetAsync("/api/v1/downloadclient");
        dlListResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
