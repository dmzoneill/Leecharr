// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Xml;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;

namespace Leecharr.Core.Test.Common;

[TestFixture]
public class SafeXmlParserTest
{
    [Test]
    public void Parse_WithValidXml_ReturnsDocument()
    {
        var xml = "<root><item id=\"1\">Value</item></root>";
        var doc = SafeXmlParser.Parse(xml);

        doc.Should().NotBeNull();
        doc.Root.Should().NotBeNull();
        doc.Root!.Element("item")?.Value.Should().Be("Value");
    }

    [Test]
    public void Parse_WithDtdProcessing_ThrowsXmlException()
    {
        var xmlWithDtd = "<!DOCTYPE foo [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><root>&xxe;</root>";
        var act = () => SafeXmlParser.Parse(xmlWithDtd);

        act.Should().Throw<XmlException>();
    }

    [Test]
    public void Parse_WithInlineDtd_ThrowsXmlException()
    {
        var xmlWithDtd = "<!DOCTYPE test [ <!ELEMENT test ANY > <!ENTITY xxe \"evil\"> ]><test>&xxe;</test>";
        var act = () => SafeXmlParser.Parse(xmlWithDtd);

        act.Should().Throw<XmlException>();
    }

    [Test]
    public void CreateSafeSettings_ConfiguresOwaspSecuritySettings()
    {
        var settings = SafeXmlParser.CreateSafeSettings();

        settings.DtdProcessing.Should().Be(DtdProcessing.Prohibit);
        settings.MaxCharactersFromEntities.Should().Be(1024);
        settings.MaxCharactersInDocument.Should().Be(20 * 1024 * 1024);
    }
}
