using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class SystemTests : IntegrationTestBase
{
    [Test]
    public async Task GetSystemStatus_ReturnsOkAndValidJson()
    {
        var response = await GetAsync("/api/v1/system/status");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Does.Contain("appName").IgnoreCase.Or.Contain("version").IgnoreCase);
    }
}
