using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Test.Automation;

[TestFixture]
public class YamlScriptRunnerTest
{
    private YamlScriptRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _runner = new YamlScriptRunner();
    }

    [Test]
    public void ShouldExecuteYamlWorkflowAndApplyActions()
    {
        var torrent = new Torrent
        {
            Id = 1,
            Name = "The.Matrix.1999.2160p",
            TotalSize = 15_000_000_000,
            Category = "Unknown",
        };

        var yaml = "name: 'Categorize and Tag'\n" +
                   "steps:\n" +
                   "  - name: 'Check Large'\n" +
                   "    condition: '${torrent.size} > 1000000000'\n" +
                   "    actions:\n" +
                   "      - addTag: '4K-UHD'\n" +
                   "      - setCategory: 'Movies'\n";

        var script = new AutomationScript
        {
            Name = "YAML Rule",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.TagsToAdd, Contains.Item("4K-UHD"));
        Assert.That(result.NewCategory, Is.EqualTo("Movies"));
    }

    [Test]
    public void ShouldExecuteYamlActionsWithRecheckAndCommand()
    {
        var torrent = new Torrent
        {
            Id = 99,
            Name = "Linux.ISO",
        };

        var yaml = "name: 'Recheck and Command'\n" +
                   "steps:\n" +
                   "  - name: 'Execute pipeline'\n" +
                   "    actions:\n" +
                   "      - command: 'Backup'\n" +
                   "      - recheck: true\n" +
                   "      - reannounce: true\n";

        var script = new AutomationScript
        {
            Name = "Pipeline",
            Code = yaml,
            Language = AutomationLanguage.Yaml,
        };

        var result = _runner.Execute(script, torrent);

        Assert.That(result.Success, Is.True);
        Assert.That(result.ShouldRecheck, Is.True);
        Assert.That(result.ShouldReannounce, Is.True);
    }
}
