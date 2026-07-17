using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Notifications;

namespace Leecharr.Core.Test.Notifications;

[TestFixture]
public class WebhookDispatcherTest
{
    private WebhookDispatcher _dispatcher = null!;

    [SetUp]
    public void SetUp()
    {
        _dispatcher = new WebhookDispatcher();
    }

    [Test]
    public async Task DispatchAsync_WhenUrlEmpty_ReturnsFalse()
    {
        var result = await _dispatcher.DispatchAsync(string.Empty, new { eventType = "Test" });
        result.Should().BeFalse();
    }

    [Test]
    public async Task DispatchAsync_WhenUnreachableUrl_ReturnsFalseAfterRetries()
    {
        // Port 1 is blocked/unreachable, will fail fast
        var result = await _dispatcher.DispatchAsync("http://127.0.0.1:1/nonexistent", new { eventType = "Test" });
        result.Should().BeFalse();
    }
}
