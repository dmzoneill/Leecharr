using System.Text;
using BencodeNET.Objects;
using NUnit.Framework;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.Torrents;

[TestFixture]
public class InfoHashCalculatorTest
{
    [Test]
    public void should_calculate_correct_sha1_hex_infohash()
    {
        var info = new BDictionary
        {
            ["name"] = new BString(Encoding.UTF8.GetBytes("sample.txt")),
            ["length"] = new BNumber(1024),
            ["piece length"] = new BNumber(512),
            ["pieces"] = new BString(new byte[40])
        };

        var hash = InfoHashCalculator.Calculate(info);

        Assert.That(hash, Is.Not.Null);
        Assert.That(hash.Length, Is.EqualTo(40));
        Assert.That(hash, Is.EqualTo(hash.ToLowerInvariant()));
    }
}
