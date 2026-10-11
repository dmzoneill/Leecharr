// Copyright (c) FeedItOut. All rights reserved.

using System.IO;
using FluentAssertions;
using NUnit.Framework;

namespace Leecharr.Core.Test.Host;

[TestFixture]
public class HostHttpsRedirectionMiddlewareOrderTest
{
    [Test]
    public void HttpsRedirection_is_registered_before_ui_shell_middleware()
    {
        var startupSource = File.ReadAllText(FindStartupSourcePath());
        var redirectIndex = startupSource.IndexOf("app.UseHttpsRedirection()", System.StringComparison.Ordinal);
        var shellIndex = startupSource.IndexOf(
            "path == \"/\" || path.Equals(\"/index.html\"",
            System.StringComparison.Ordinal);

        redirectIndex.Should().BeGreaterThan(0);
        shellIndex.Should().BeGreaterThan(0);
        redirectIndex.Should().BeLessThan(shellIndex);
    }

    private static string FindStartupSourcePath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "NzbDrone.Host", "Startup.cs");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate NzbDrone.Host/Startup.cs from test output directory.");
    }
}
