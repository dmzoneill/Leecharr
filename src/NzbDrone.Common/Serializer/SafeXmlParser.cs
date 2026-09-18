// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace NzbDrone.Common.Serializer;

public static class SafeXmlParser
{
    private const long MaxDocCharacters = 20 * 1024 * 1024; // 20 MB

    public static XmlReaderSettings CreateSafeSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = MaxDocCharacters,
        };
    }

    public static XDocument Parse(string xml, LoadOptions options = LoadOptions.None)
    {
        ArgumentNullException.ThrowIfNull(xml);

        var settings = CreateSafeSettings();
        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader, options);
    }

    public static XDocument Load(TextReader reader, LoadOptions options = LoadOptions.None)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var settings = CreateSafeSettings();
        using var xmlReader = XmlReader.Create(reader, settings);
        return XDocument.Load(xmlReader, options);
    }

    public static XDocument Load(Stream stream, LoadOptions options = LoadOptions.None)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var settings = CreateSafeSettings();
        using var xmlReader = XmlReader.Create(stream, settings);
        return XDocument.Load(xmlReader, options);
    }
}
