using System.Linq;
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

        Assert.That(requests.Count, Is.EqualTo(2));
        // Piece 3 is rarer than Piece 5, so Piece 3 must be picked first!
        Assert.That(requests.All(r => r.PieceIndex == 3), Is.True);
    }

    [Test]
    public void should_prioritize_head_and_tail_in_sequential_mode()
    {
        // 20 pieces, 16KB each
        var picker = new PiecePicker(20, 16384, 327680);
        var fullBitfield = Enumerable.Repeat(true, 20).ToArray();

        var requests = picker.PickBlocks(fullBitfield, 6, sequentialMode: true);

        Assert.That(requests.Count, Is.EqualTo(6));
        // First 4 requests should be head pieces (0, 1, 2, 3) and next 2 should be tail (18, 19)
        var pickedPieces = requests.Select(r => r.PieceIndex).Distinct().ToList();
        Assert.That(pickedPieces, Does.Contain(0));
        Assert.That(pickedPieces, Does.Contain(18).Or.Contain(19));
    }

    [Test]
    public void should_complete_piece_when_all_blocks_received()
    {
        // 1 piece with 2 blocks (32KB piece, 16KB blocks)
        var picker = new PiecePicker(1, 32768, 32768);

        var firstDone = picker.MarkBlockReceived(0, 0, 16384);
        Assert.That(firstDone, Is.False);

        var secondDone = picker.MarkBlockReceived(0, 16384, 16384);
        Assert.That(secondDone, Is.True);

        picker.MarkPieceVerified(0);
        Assert.That(picker.GetProgress(), Is.EqualTo(1.0));
    }
}
