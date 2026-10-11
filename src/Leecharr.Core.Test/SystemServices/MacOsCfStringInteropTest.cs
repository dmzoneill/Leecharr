// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Runtime.InteropServices;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.SystemServices;

namespace Leecharr.Core.Test.SystemServices;

[TestFixture]
public class MacOsCfStringInteropTest
{
    [Test]
    public void Create_PassesUtf16BufferAndFullCodeUnitCount()
    {
        const string assertionType = "PreventUserIdleSystemSleep";
        var capturedCharacters = IntPtr.Zero;
        var capturedLength = IntPtr.Zero;

        MacOsCfStringInterop.CreateWithCharactersOverride = (alloc, characters, length) =>
        {
            capturedCharacters = characters;
            capturedLength = length;
            return new IntPtr(1);
        };

        try
        {
            var result = MacOsCfStringInterop.Create(assertionType);

            result.Should().Be(new IntPtr(1));
            capturedLength.Should().Be((IntPtr)assertionType.Length);
            Marshal.ReadInt16(capturedCharacters).Should().Be((short)'P');
            Marshal.ReadInt16(capturedCharacters, 2).Should().Be((short)'r');
        }
        finally
        {
            MacOsCfStringInterop.CreateWithCharactersOverride = null;
        }
    }
}
