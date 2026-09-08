// Copyright (c) PlaceholderCompany. All rights reserved.

using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;

namespace Leecharr.Core.Test.Organizer;

[TestFixture]
public class FileNameSanitizerTest
{
    private FileNameSanitizer sanitizer = null!;

    [SetUp]
    public void SetUp()
    {
        this.sanitizer = new FileNameSanitizer();
    }

    [TestCase("Movie: The Beginning.mkv", ColonReplacementFormat.SpaceDashSpace, "Movie - The Beginning.mkv")]
    [TestCase("Movie: The Beginning.mkv", ColonReplacementFormat.Dash, "Movie- The Beginning.mkv")]
    [TestCase("Movie: The Beginning.mkv", ColonReplacementFormat.Delete, "Movie The Beginning.mkv")]
    [TestCase("Movie: The Beginning.mkv", ColonReplacementFormat.SpaceDash, "Movie - The Beginning.mkv")]
    [TestCase("10:00 AM: The Movie.mkv", ColonReplacementFormat.Smart, "10-00 AM - The Movie.mkv")]
    public void SanitizeFileName_ColonReplacement_FormatsCorrectly(
        string input,
        ColonReplacementFormat format,
        string expected)
    {
        var result = this.sanitizer.SanitizeFileName(input, format);
        result.Should().Be(expected);
    }

    [TestCase("Movie*Title?.mkv", "MovieTitle.mkv")]
    [TestCase("Movie<Title>|Part\"1.mkv", "MovieTitlePart1.mkv")]
    [TestCase("Movie/Title\\Sub.mkv", "Movie-Title-Sub.mkv")]
    [TestCase("Movie\u0000Title\u001F.mkv", "MovieTitle.mkv")]
    public void SanitizeFileName_ForbiddenCharacters_StripsOrNeutralizes(string input, string expected)
    {
        var result = this.sanitizer.SanitizeFileName(input);
        result.Should().Be(expected);
    }

    [TestCase("Movie Name . . .mkv", "Movie Name.mkv")]
    [TestCase("Movie Name   .mkv", "Movie Name.mkv")]
    [TestCase("Movie Name....", "Movie Name")]
    [TestCase("  Movie Name  .mkv", "Movie Name.mkv")]
    public void SanitizeFileName_TrailingDotsAndSpaces_StripsCorrectly(string input, string expected)
    {
        var result = this.sanitizer.SanitizeFileName(input);
        result.Should().Be(expected);
    }

    [TestCase("CON.mkv", "_CON.mkv")]
    [TestCase("aux.mp4", "_aux.mp4")]
    [TestCase("prn.txt", "_prn.txt")]
    [TestCase("nul.avi", "_nul.avi")]
    [TestCase("COM1.mkv", "_COM1.mkv")]
    [TestCase("LPT9.mkv", "_LPT9.mkv")]
    [TestCase("aux", "_aux")]
    public void SanitizeFileName_DosReservedNames_EscapesWithUnderscore(string input, string expected)
    {
        var result = this.sanitizer.SanitizeFileName(input);
        result.Should().Be(expected);
    }

    [TestCase("Folder . .", "Folder")]
    [TestCase("Folder: Name", "Folder - Name")]
    [TestCase("CON", "_CON")]
    [TestCase("Folder*Name?<>|", "FolderName")]
    public void SanitizeFolderName_SanitizesCorrectly(string input, string expected)
    {
        var result = this.sanitizer.SanitizeFolderName(input);
        result.Should().Be(expected);
    }

    [TestCase("Marvel's Agents of S.H.I.E.L.D.", "Marvels Agents of S.H.I.E.L.D")]
    [TestCase("Star Wars: Episode IV - A New Hope", "Star Wars Episode IV - A New Hope")]
    [TestCase("Fast & Furious", "Fast and Furious")]
    [TestCase("Movie`s   Title?", "Movies Title")]
    public void CleanTitle_CleansCorrectly(string input, string expected)
    {
        var result = this.sanitizer.CleanTitle(input);
        result.Should().Be(expected);
    }

    [TestCase("Valid_FileName (2024) [1080p].mkv", true)]
    [TestCase("Invalid:Name.mkv", false)]
    [TestCase("Invalid*Name.mkv", false)]
    [TestCase("Invalid?Name.mkv", false)]
    [TestCase("Invalid\"Name.mkv", false)]
    [TestCase("Invalid<Name>.mkv", false)]
    [TestCase("Invalid|Name.mkv", false)]
    [TestCase("TrailingDot.", false)]
    [TestCase("TrailingSpace ", false)]
    [TestCase("CON.mkv", false)]
    [TestCase("aux.mkv", false)]
    [TestCase("..", false)]
    [TestCase(".", false)]
    [TestCase("", false)]
    public void IsValidFileName_ValidatesCorrectly(string input, bool expected)
    {
        var result = this.sanitizer.IsValidFileName(input);
        result.Should().Be(expected);
    }

    [TestCase("Movies/Action/Film (2024)/Film.mkv", true)]
    [TestCase("Movies/Action:/Film.mkv", false)]
    [TestCase("Movies/CON/Film.mkv", false)]
    [TestCase("Movies/../Film.mkv", false)]
    public void IsValidPath_ValidatesCorrectly(string input, bool expected)
    {
        var result = this.sanitizer.IsValidPath(input);
        result.Should().Be(expected);
    }
}
