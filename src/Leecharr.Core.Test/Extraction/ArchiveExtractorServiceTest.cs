using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extraction;

namespace Leecharr.Core.Test.Extraction;

[TestFixture]
public class ArchiveExtractorServiceTest
{
    private IDiskProvider _diskProvider = null!;
    private ArchiveExtractorService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _diskProvider = Substitute.For<IDiskProvider>();
        _service = new ArchiveExtractorService(_diskProvider);
    }

    [TestCase("sample.zip", true)]
    [TestCase("sample.rar", true)]
    [TestCase("sample.7z", true)]
    [TestCase("sample.tar", true)]
    [TestCase("sample.mkv", false)]
    [TestCase("sample.txt", false)]
    public void IsArchiveFile_DetectsSupportedExtensions(string fileName, bool expected)
    {
        _service.IsArchiveFile(fileName).Should().Be(expected);
    }

    [Test]
    public async Task ExtractArchiveAsync_WhenFileDoesNotExist_ReturnsFalse()
    {
        _diskProvider.FileExists("/path/to/missing.zip").Returns(false);

        var result = await _service.ExtractArchiveAsync("/path/to/missing.zip");
        result.Should().BeFalse();
    }
}
