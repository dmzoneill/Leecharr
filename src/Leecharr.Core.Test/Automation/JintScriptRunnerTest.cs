using System.Collections.Generic;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class JintScriptRunnerTest
{
    private JintScriptRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _runner = new JintScriptRunner();
    }

    [Test]
    public void ShouldExecuteBasicJavascript()
    {
        var script = new AutomationScript
        {
            Name = "Basic Math",
            Code = "console.log('Result:', 2 + 2);",
            Language = AutomationLanguage.JavaScript,
        };

        var result = _runner.Execute(script);

        Assert.That(result.Success, Is.True);
        Assert.That(result.OutputLog, Does.Contain("Result: 4"));
    }

    [Test]
    public void ShouldReadAndMutateTorrentContext()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "Big.Buck.Bunny.1080p",
            TotalSize = 2_000_000_000,
            Ratio = 2.5,
            Category = "Movies",
        };

        var script = new AutomationScript
        {
            Name = "Torrent Mutator",
            Code = @"
console.log('Torrent:', torrent.name, 'Ratio:', torrent.ratio);
if (torrent.ratio > 2.0) {
    torrent.addTag('high-ratio');
    torrent.setCategory('Seeded');
    torrent.pause();
}
",
            Language = AutomationLanguage.JavaScript,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("high-ratio"));
        Assert.That(result.NewCategory, Is.EqualTo("Seeded"));
        Assert.That(result.ShouldPause, Is.True);
    }

    [Test]
    public void ShouldReadInputsAndSecrets()
    {
        var script = new AutomationScript
        {
            Name = "Inputs Reader",
            Code = "console.log('API Key:', inputs.api_key, 'Target:', inputs.target);",
            InputsJson = "{\"api_key\": \"secret-123\", \"target\": 999}",
            Language = AutomationLanguage.JavaScript,
        };

        var result = _runner.Execute(script);

        Assert.That(result.Success, Is.True);
        Assert.That(result.OutputLog, Does.Contain("API Key: secret-123 Target: 999"));
    }

    [Test]
    public void ShouldHandleScriptSyntaxErrorGracefully()
    {
        var script = new AutomationScript
        {
            Name = "Invalid Script",
            Code = "invalid syntax { [",
            Language = AutomationLanguage.JavaScript,
        };

        var result = _runner.Execute(script);

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Is.Not.Null);
    }

    [Test]
    public void ShouldExecuteTorrentRecheckAndReannounceActions()
    {
        var torrent = new Torrent
        {
            Id = 10,
            Name = "Sample.Torrent",
        };

        var script = new AutomationScript
        {
            Name = "Recheck & Reannounce",
            Code = @"
torrent.recheck();
torrent.reannounce();
",
            Language = AutomationLanguage.JavaScript,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldRecheck, Is.True);
        Assert.That(result.ShouldReannounce, Is.True);
    }

    [Test]
    public void ShouldExecuteSystemContextGracefullyWithoutQueue()
    {
        var script = new AutomationScript
        {
            Name = "System Task Runner",
            Code = @"
var cmd = system.runCommand('Backup');
console.log('Command queued:', cmd);
",
            Language = AutomationLanguage.JavaScript,
        };

        var result = _runner.Execute(script);

        Assert.That(result.Success, Is.True);
        Assert.That(result.OutputLog, Does.Contain("Command queued: null"));
    }
}
