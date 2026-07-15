using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.BitTorrent;

namespace Leecharr.Core.Test.BitTorrent;

[TestFixture]
public class PiecePickerTest
{
    [Test]
    public void should_pick_rarest_pieces_first()
    {
        // 10 pieces, 32KB each, total 320KB
        var picker = new PiecePicker(10, 32768, 327680);

        // Peer 1 has pieces 0..9
        var peer1Bitfield = Enumerable.Repeat(true, 10).ToArray();
        picker.UpdatePeerAvailability(peer1Bitfield, true);

        // Peer 2 has only piece 5
        var peer2Bitfield = new bool[10];
        peer2Bitfield[5] = true;
        picker.UpdatePeerAvailability(peer2Bitfield, true);

        // Pieces 0..4 and 6..9 have rarity 1; Piece 5 has rarity 2.
        // Peer 3 has pieces 3 and 5.
        var peer3Bitfield = new bool[10];
        peer3Bitfield[3] = true;
        peer3Bitfield[5] = true;

        var requests = picker.PickBlocks(peer3Bitfield, 2);

        requests.Should().HaveCount(2);
        // Piece 3 is rarer than Piece 5, so Piece 3 must be picked first!
        requests.All(r => r.PieceIndex == 3).Should().BeTrue();
    }

    [Test]
    public void should_prioritize_head_and_tail_in_sequential_mode()
    {
        // 20 pieces, 16KB each
        var picker = new PiecePicker(20, 16384, 327680);
        var fullBitfield = Enumerable.Repeat(true, 20).ToArray();

        var requests = picker.PickBlocks(fullBitfield, 6, sequentialMode: true);

        requests.Should().HaveCount(6);
        var pickedPieces = requests.Select(r => r.PieceIndex).Distinct().ToList();
        pickedPieces.Should().Contain(0);
        pickedPieces.Any(p => p >= 18).Should().BeTrue();
    }

    [Test]
    public void should_complete_piece_when_all_blocks_received()
    {
        // 1 piece with 2 blocks (32KB piece, 16KB blocks)
        var picker = new PiecePicker(1, 32768, 32768);

        var firstDone = picker.MarkBlockReceived(0, 0, 16384);
        firstDone.Should().BeFalse();

        var secondDone = picker.MarkBlockReceived(0, 16384, 16384);
        secondDone.Should().BeTrue();

        picker.MarkPieceVerified(0);
        picker.GetProgress().Should().Be(1.0);
    }

    [Test]
    public void should_skip_pieces_with_zero_priority()
    {
        var picker = new PiecePicker(5, 16384, 81920);
        picker.SetPiecePriority(0, 0); // Skip piece 0

        var fullBitfield = Enumerable.Repeat(true, 5).ToArray();
        var requests = picker.PickBlocks(fullBitfield, 10);

        requests.Should().NotBeEmpty();
        requests.Any(r => r.PieceIndex == 0).Should().BeFalse();
    }

    [Test]
    public void should_prioritize_high_priority_pieces()
    {
        var picker = new PiecePicker(5, 16384, 81920);
        picker.SetPiecePriority(3, 3); // Max priority on piece 3

        var fullBitfield = Enumerable.Repeat(true, 5).ToArray();
        var requests = picker.PickBlocks(fullBitfield, 1);

        requests.Should().HaveCount(1);
        requests[0].PieceIndex.Should().Be(3);
    }

    [Test]
    public void should_reset_corrupted_piece_when_verification_fails()
    {
        var picker = new PiecePicker(2, 16384, 32768);
        picker.MarkBlockReceived(0, 0, 16384);

        // Reset piece 0
        picker.MarkPieceCorrupt(0);

        var bitfield = picker.GetBitfield();
        bitfield[0].Should().BeFalse();
        picker.GetProgress().Should().Be(0.0);
    }

    [Test]
    public void should_handle_peer_disconnect_and_decrease_availability()
    {
        var picker = new PiecePicker(5, 16384, 81920);
        var peerBitfield = new bool[] { true, true, false, false, false };

        picker.UpdatePeerAvailability(peerBitfield, isAdd: true);
        picker.UpdatePeerAvailability(peerBitfield, isAdd: false);

        // Availability should not go negative
        var requests = picker.PickBlocks(peerBitfield, 2);
        requests.Should().NotBeNull();
    }

    [Test]
    public void should_cancel_in_flight_block_request()
    {
        var picker = new PiecePicker(2, 16384, 32768);
        var fullBitfield = new bool[] { true, true };

        var requests1 = picker.PickBlocks(fullBitfield, 1);
        requests1.Should().HaveCount(1);

        // Cancel the block request
        picker.CancelBlock(requests1[0].PieceIndex, requests1[0].BlockOffset);

        // Block can be requested again
        var requests2 = picker.PickBlocks(fullBitfield, 1);
        requests2.Should().HaveCount(1);
        requests2[0].PieceIndex.Should().Be(requests1[0].PieceIndex);
    }

    [Test]
    public void should_return_empty_when_peer_has_no_pieces()
    {
        var picker = new PiecePicker(5, 16384, 81920);
        var emptyBitfield = new bool[5];

        var requests = picker.PickBlocks(emptyBitfield, 5);
        requests.Should().BeEmpty();
    }
}
