using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[SetUpFixture]
public class GlobalSetup
{
    public static LeecharrWebApplicationFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Factory = new LeecharrWebApplicationFactory();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Factory?.Dispose();
    }
}
