// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Instrumentation;

namespace Leecharr.Core.Test.Instrumentation;

[TestFixture]
public class FileDescriptorProviderTest
{
    private string tempDir;
    private string limitsFile;
    private string fdDir;

    [SetUp]
    public void SetUp()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), "fd_test_" + Guid.NewGuid().ToString("N"));
        this.limitsFile = Path.Combine(this.tempDir, "limits");
        this.fdDir = Path.Combine(this.tempDir, "fd");

        Directory.CreateDirectory(this.tempDir);
        Directory.CreateDirectory(this.fdDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(this.tempDir))
            {
                Directory.Delete(this.tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures in test temp dir
        }
    }

    [Test]
    public void GetMaxFileDescriptors_ReturnsZeroWhenLimitsFileMissing()
    {
        var nonExistentPath = Path.Combine(this.tempDir, "does_not_exist");
        var provider = new FileDescriptorProvider(this.fdDir, nonExistentPath, () => 42, () => false);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(0));
    }

    [Test]
    public void GetMaxFileDescriptors_ReturnsNegativeOneWhenUnlimited()
    {
        var limitsContent = "Max open files            unlimited            unlimited            files     ";
        File.WriteAllText(this.limitsFile, limitsContent);

        var provider = new FileDescriptorProvider(this.fdDir, this.limitsFile, () => 42, () => false);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(-1));
    }

    [Test]
    public void GetMaxFileDescriptors_ReturnsZeroOnWindows()
    {
        File.WriteAllText(this.limitsFile, "Max open files            1024                 524288               files     ");

        var provider = new FileDescriptorProvider(this.fdDir, this.limitsFile, () => 42, () => true);

        Assert.That(provider.GetMaxFileDescriptors(), Is.EqualTo(0));
    }

    [Test]
    public void GetFileDescriptorUsagePercentage_ReturnsNullWhenMaxUnknown()
    {
        var provider = new FileDescriptorProvider(this.fdDir, this.limitsFile, () => 42, () => true);

        Assert.That(provider.GetFileDescriptorUsagePercentage(), Is.Null);
    }

    [Test]
    public void GetFileDescriptorUsagePercentage_ReturnsNullWhenMaxUnlimited()
    {
        File.WriteAllText(this.limitsFile, "Max open files            unlimited            unlimited            files     ");

        var provider = new FileDescriptorProvider(this.fdDir, this.limitsFile, () => 42, () => false);

        Assert.That(provider.GetFileDescriptorUsagePercentage(), Is.Null);
    }

    [Test]
    public void GetFileDescriptorUsagePercentage_CalculatesCorrectPercentage()
    {
        for (var i = 0; i < 256; i++)
        {
            File.WriteAllText(Path.Combine(this.fdDir, i.ToString()), string.Empty);
        }

        File.WriteAllText(this.limitsFile, "Max open files            1024                 524288               files     ");

        var provider = new FileDescriptorProvider(this.fdDir, this.limitsFile, () => 42, () => false);

        var percentage = provider.GetFileDescriptorUsagePercentage();
        Assert.That(percentage, Is.Not.Null);
        Assert.That(percentage.Value, Is.EqualTo(25.0));
    }
}
