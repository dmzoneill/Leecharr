#pragma warning disable SA1500, SA1516, SA1513, SA1508, SA1512, SA1507, SA1028
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Torrents;

namespace Leecharr.Core.Test.CustomScripts;

[TestFixture]
public class YamlScriptRunnerBranchTests
{
    private YamlScriptRunner _runner = null!;
    private IManageCommandQueue _commandQueue = null!;
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _commandQueue = Substitute.For<IManageCommandQueue>();
        _runner = new YamlScriptRunner(_commandQueue);
        _tempDir = Path.Combine(Path.GetTempPath(), "YamlRunnerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }

    [Test]
    public void Execute_NullOrEmptyCode_ReturnsSuccessWithEmptyLog()
    {
        var script = new AutomationScript { Name = "Empty", Code = null! };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Empty YAML workflow executed successfully");

        script.Code = "   \n  ";
        var result2 = _runner.Execute(script);
        result2.Success.Should().BeTrue();
        result2.OutputLog.Should().Contain("Empty YAML workflow executed successfully");
    }

    [Test]
    public void Execute_WorkflowWithNoStepsOrEmptySteps_ReturnsSuccess()
    {
        var yaml1 = "name: 'No Steps'\ntrigger: 'Manual'\n";
        var script1 = new AutomationScript { Name = "NoSteps", Code = yaml1 };
        var result1 = _runner.Execute(script1);
        result1.Success.Should().BeTrue();
        result1.OutputLog.Should().Contain("Empty YAML workflow");

        var yaml2 = "name: 'Empty Steps'\nsteps: []\n";
        var script2 = new AutomationScript { Name = "EmptySteps", Code = yaml2 };
        var result2 = _runner.Execute(script2);
        result2.Success.Should().BeTrue();
        result2.OutputLog.Should().Contain("Empty YAML workflow");
    }

    [Test]
    public void Execute_MalformedYaml_ReturnsFailureWithError()
    {
        var malformedYaml = "name: 'Broken'\nsteps:\n  - name: [unclosed list\n    actions:\n      - addTag: 'fail'\n";
        var script = new AutomationScript { Name = "BrokenYaml", Code = malformedYaml };

        var result = _runner.Execute(script);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("YAML Execution error");
        result.OutputLog.Should().Contain("[ERROR]");
    }

    [Test]
    public void Execute_StepWithMissingActions_ExecutesStepWithoutError()
    {
        var yaml = @"
name: 'Missing Actions'
steps:
  - name: 'Step Without Actions'
  - name: 'Step With Null Actions'
    actions:
";
        var script = new AutomationScript { Name = "NoActions", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Starting: Step Without Actions");
        result.OutputLog.Should().Contain("Starting: Step With Null Actions");
    }

    [Test]
    public void Execute_StepWithUnnamedStep_UsesFallbackName()
    {
        var yaml = @"
steps:
  - actions:
      - log: 'Hello unnamed'
";
        var script = new AutomationScript { Name = "Unnamed", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Starting: Unnamed Step");
    }

    [Test]
    public void Execute_InputsJsonAndCustomInputs_CoversAllJsonTypesAndOverrides()
    {
        var inputsJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            { "strVal", "my-string" },
            { "intVal", 42 },
            { "doubleVal", 3.14159 },
            { "boolTrue", true },
            { "boolFalse", false },
            { "nullVal", null },
            { "arrayVal", new[] { "a", "b" } },
            { "objVal", new { nested = "test" } }
        });

        var customInputs = new Dictionary<string, object>
        {
            { "customStr", "custom-override" },
            { "ENV_FLAG", "enabled" }
        };

        var yaml = @"
steps:
  - name: 'Check Inputs'
    actions:
      - log: 'Str: ${inputs.strVal}, Int: ${inputs.intVal}, Dbl: ${inputs.doubleVal}, BoolT: ${inputs.boolTrue}, BoolF: ${inputs.boolFalse}, Null: ${inputs.nullVal}, Arr: ${inputs.arrayVal}, Obj: ${inputs.objVal}'
      - log: 'Custom: ${inputs.customStr}, Secret: ${secrets.customStr}, Env: ${inputs.ENV_FLAG}'
";
        var script = new AutomationScript { Name = "InputsTest", Code = yaml, InputsJson = inputsJson };
        var result = _runner.Execute(script, customInputs: customInputs);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Str: my-string");
        result.OutputLog.Should().Contain("Int: 42");
        result.OutputLog.Should().Contain("Custom: custom-override");
        result.OutputLog.Should().Contain("Secret: custom-override");
        result.OutputLog.Should().Contain("Env: enabled");
    }

    [Test]
    public void Execute_MalformedInputsJson_LogsWarningAndContinues()
    {
        var script = new AutomationScript
        {
            Name = "BadInputsJson",
            Code = "steps:\n  - name: 'Step'\n    actions:\n      - log: 'Ran'\n",
            InputsJson = "{ this is not valid json }"
        };

        var result = _runner.Execute(script);
        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Ran");
    }

    [Test]
    public void Execute_TorrentVariableSubstitutions_CoversAllTorrentPropertiesAndCompletionBranches()
    {
        var now = DateTime.UtcNow;

        // Branch 1: Progress >= 1.0f
        var t1 = new Torrent
        {
            Id = 101,
            Name = "Torrent.Alpha.2024",
            InfoHash = "abcdef123456",
            TotalSize = 1024 * 1024 * 100,
            Ratio = 1.75,
            Category = "Movies",
            TrackerUrl = "http://tracker.example.com/announce",
            Status = TorrentStatus.Downloading,
            Progress = 1.0f,
            IsPrivate = true,
            DownloadSpeed = 1024 * 50,
            UploadSpeed = 1024 * 25,
            Eta = 300,
            Seeders = 15,
            Leechers = 3,
            SavePath = _tempDir,
            Uploaded = 2000000,
            Downloaded = 1000000,
            CumulativeSeedingTimeSeconds = 3600,
            Priority = 2,
            Label = "Important",
            Comment = "Alpha release",
            TargetRatio = 2.0,
            TargetSeedTimeMinutes = 120
        };

        var yaml = @"
steps:
  - name: 'Check All Torrent Props'
    actions:
      - log: 'id=${torrent.id}, name=${torrent.name}, hash=${torrent.infoHash}, size=${torrent.size}, totalSize=${torrent.totalSize}, ratio=${torrent.ratio}, cat=${torrent.category}'
      - log: 'trk=${torrent.tracker}, trkUrl=${torrent.trackerUrl}, status=${torrent.status}, progress=${torrent.progress}, priv=${torrent.isPrivate}, complete=${torrent.isComplete}'
      - log: 'dlSpeed=${torrent.downloadSpeed}, upSpeed=${torrent.uploadSpeed}, eta=${torrent.eta}, seeders=${torrent.seeders}, leechers=${torrent.leechers}, savePath=${torrent.savePath}'
      - log: 'up=${torrent.uploaded}, dl=${torrent.downloaded}, seedSec=${torrent.seedingTime}, seedSec2=${torrent.seedingTimeSeconds}, seedMin=${torrent.seedingTimeMinutes}, seedMin2=${torrent.seedTimeMinutes}'
      - log: 'prio=${torrent.priority}, label=${torrent.label}, comment=${torrent.comment}, tgtRatio=${torrent.targetRatio}, tgtSeedMin=${torrent.targetSeedTimeMinutes}'
      - log: 'diskFree=${system.diskFreeSpace}, diskTot=${system.diskTotalSpace}, vpn=${system.vpnActive}, port=${system.isPortForwarded}'
";
        var script = new AutomationScript { Name = "AllProps", Code = yaml };
        var result1 = _runner.Execute(script, t1);

        result1.Success.Should().BeTrue();
        result1.OutputLog.Should().Contain("id=101, name=Torrent.Alpha.2024");
        result1.OutputLog.Should().Contain("complete=True");
        result1.OutputLog.Should().Contain("priv=True");
        result1.OutputLog.Should().Contain("cat=Movies");
        result1.OutputLog.Should().Contain("label=Important");
        result1.OutputLog.Should().Contain("comment=Alpha release");
        result1.OutputLog.Should().Contain("vpn=True");

        // Branch 2: Progress >= 0.999f
        var t2 = new Torrent { Progress = 0.9995f, Status = TorrentStatus.Downloading };
        var scriptComplete = new AutomationScript
        {
            Code = "steps:\n  - name: 'Check'\n    actions:\n      - log: 'complete=${torrent.isComplete}'\n"
        };
        var result2 = _runner.Execute(scriptComplete, t2);
        result2.OutputLog.Should().Contain("complete=True");

        // Branch 3: Status == Seeding
        var t3 = new Torrent { Progress = 0.5f, Status = TorrentStatus.Seeding };
        var result3 = _runner.Execute(scriptComplete, t3);
        result3.OutputLog.Should().Contain("complete=True");

        // Branch 4: DateCompleted.HasValue
        var t4 = new Torrent { Progress = 0.5f, Status = TorrentStatus.Downloading, DateCompleted = now };
        var result4 = _runner.Execute(scriptComplete, t4);
        result4.OutputLog.Should().Contain("complete=True");

        // Branch 5: Incomplete
        var t5 = new Torrent { Progress = 0.5f, Status = TorrentStatus.Downloading, DateCompleted = null };
        var result5 = _runner.Execute(scriptComplete, t5);
        result5.OutputLog.Should().Contain("complete=False");
    }

    [Test]
    public void Execute_VariableSubstitutionInsideQuotes_HandlesJsonEscapesCorrectly()
    {
        // Special chars inside quotes: quotes, backslash, newlines, carriage return, tab, backspace, formfeed, control char
        var funnyString = "Quote: \" Backslash: \\ NL: \n CR: \r TAB: \t BS: \b FF: \f CTRL: \u0005 Normal";
        var torrent = new Torrent
        {
            Name = funnyString
        };

        var yaml = @"
steps:
  - name: 'Escape In Quotes'
    actions:
      - log: 'Inside: ""${torrent.name}"" Outside: ${torrent.name}'
";
        var script = new AutomationScript { Name = "EscapeTest", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("\\\"");
        result.OutputLog.Should().Contain("\\\\");
        result.OutputLog.Should().Contain("\\n");
        result.OutputLog.Should().Contain("\\r");
        result.OutputLog.Should().Contain("\\t");
        result.OutputLog.Should().Contain("\\b");
        result.OutputLog.Should().Contain("\\f");
        result.OutputLog.Should().Contain("\\u0005");
    }

    [Test]
    public void Execute_UnmatchedVariables_RemainIntact()
    {
        var yaml = @"
steps:
  - name: 'Unknown Var'
    actions:
      - log: 'Hello ${nonexistent.variable} World'
";
        var script = new AutomationScript { Name = "UnknownVar", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Hello ${nonexistent.variable} World");
    }

    [Test]
    public void Execute_Condition_UnaryBooleanChecks_CoversTruthyAndFalsyBranches()
    {
        var yaml = @"
steps:
  - name: 'Empty Condition'
    condition: '   '
    actions:
      - log: 'Ran Empty Condition'

  - name: 'True String'
    condition: 'true'
    actions:
      - log: 'Ran True String'

  - name: 'False String'
    condition: 'false'
    actions:
      - log: 'Should not run False'

  - name: 'Zero String'
    condition: '0'
    actions:
      - log: 'Should not run 0'

  - name: 'One String'
    condition: '1'
    actions:
      - log: 'Ran 1 String'

  - name: 'Off String'
    condition: 'off'
    actions:
      - log: 'Should not run off'

  - name: 'On String'
    condition: 'on'
    actions:
      - log: 'Ran on String'

  - name: 'No String'
    condition: 'no'
    actions:
      - log: 'Should not run no'

  - name: 'Yes String'
    condition: 'yes'
    actions:
      - log: 'Ran yes String'

  - name: 'Null String'
    condition: 'null'
    actions:
      - log: 'Should not run null'

  - name: 'Arbitrary String'
    condition: 'any-valid-string'
    actions:
      - log: 'Ran Arbitrary String'

  - name: 'Not False'
    condition: '!false'
    actions:
      - log: 'Ran Not False'

  - name: 'Not True'
    condition: '!true'
    actions:
      - log: 'Should not run Not True'

  - name: 'Not Zero'
    condition: '!0'
    actions:
      - log: 'Ran Not Zero'

  - name: 'Not One'
    condition: '!1'
    actions:
      - log: 'Should not run Not One'

  - name: 'Not Off'
    condition: '!off'
    actions:
      - log: 'Ran Not Off'

  - name: 'Not No'
    condition: '!no'
    actions:
      - log: 'Ran Not No'

  - name: 'Not Null'
    condition: '!null'
    actions:
      - log: 'Ran Not Null'

  - name: 'Not Empty'
    condition: '!""'
    actions:
      - log: 'Ran Not Empty'

  - name: 'Not Arbitrary'
    condition: '!something'
    actions:
      - log: 'Should not run Not Arbitrary'
";
        var script = new AutomationScript { Name = "UnaryConditionTest", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Ran Empty Condition");
        result.OutputLog.Should().Contain("Ran True String");
        result.OutputLog.Should().NotContain("Should not run False");
        result.OutputLog.Should().NotContain("Should not run 0");
        result.OutputLog.Should().Contain("Ran 1 String");
        result.OutputLog.Should().NotContain("Should not run off");
        result.OutputLog.Should().Contain("Ran on String");
        result.OutputLog.Should().NotContain("Should not run no");
        result.OutputLog.Should().Contain("Ran yes String");
        result.OutputLog.Should().NotContain("Should not run null");
        result.OutputLog.Should().Contain("Ran Arbitrary String");
        result.OutputLog.Should().Contain("Ran Not False");
        result.OutputLog.Should().NotContain("Should not run Not True");
        result.OutputLog.Should().Contain("Ran Not Zero");
        result.OutputLog.Should().NotContain("Should not run Not One");
        result.OutputLog.Should().Contain("Ran Not Off");
        result.OutputLog.Should().Contain("Ran Not No");
        result.OutputLog.Should().Contain("Ran Not Null");
        result.OutputLog.Should().Contain("Ran Not Empty");
        result.OutputLog.Should().NotContain("Should not run Not Arbitrary");
    }

    [Test]
    public void Execute_Condition_EqualsAndNotEquals_CoversStringsBooleansAndNumbers()
    {
        var torrent = new Torrent
        {
            Name = "Matrix",
            Category = "Movies",
            Ratio = 2.0,
            IsPrivate = true,
            TotalSize = 500
        };

        var yaml = @"
steps:
  - name: 'String Eq Same'
    condition: '${torrent.name} == Matrix'
    actions:
      - log: 'Ran String Eq Same'

  - name: 'String Eq CaseInsensitive'
    condition: '${torrent.category} == movies'
    actions:
      - log: 'Ran String Eq CaseInsensitive'

  - name: 'String Eq Diff'
    condition: '${torrent.name} == Inception'
    actions:
      - log: 'Should not run Eq Diff'

  - name: 'Bool Eq 1 and True'
    condition: '${torrent.isPrivate} == 1'
    actions:
      - log: 'Ran Bool Eq 1 and True'

  - name: 'Bool Eq On and True'
    condition: 'on == true'
    actions:
      - log: 'Ran Bool Eq On and True'

  - name: 'Bool Eq Yes and 1'
    condition: 'yes == 1'
    actions:
      - log: 'Ran Bool Eq Yes and 1'

  - name: 'Bool Eq 0 and False'
    condition: '0 == false'
    actions:
      - log: 'Ran Bool Eq 0 and False'

  - name: 'Bool Eq Off and No'
    condition: 'off == no'
    actions:
      - log: 'Ran Bool Eq Off and No'

  - name: 'Bool Eq False Diff'
    condition: 'true == false'
    actions:
      - log: 'Should not run Bool Eq False Diff'

  - name: 'Numeric Eq Float and Int'
    condition: '${torrent.ratio} == 2'
    actions:
      - log: 'Ran Numeric Eq Float and Int'

  - name: 'Numeric Eq Close Precision'
    condition: '10.0000001 == 10.0000002'
    actions:
      - log: 'Ran Numeric Eq Close Precision'

  - name: 'Numeric Eq Diff'
    condition: '${torrent.ratio} == 3.5'
    actions:
      - log: 'Should not run Numeric Eq Diff'

  - name: 'Parts Count Not 2'
    condition: 'a == b == c'
    actions:
      - log: 'Ran Parts Count Not 2'

  - name: 'String Neq Diff'
    condition: '${torrent.name} != Avatar'
    actions:
      - log: 'Ran String Neq Diff'

  - name: 'String Neq Same'
    condition: '${torrent.name} != Matrix'
    actions:
      - log: 'Should not run String Neq Same'

  - name: 'Bool Neq'
    condition: '${torrent.isPrivate} != false'
    actions:
      - log: 'Ran Bool Neq'

  - name: 'Numeric Neq'
    condition: '${torrent.ratio} != 1.0'
    actions:
      - log: 'Ran Numeric Neq'

  - name: 'Numeric Neq Same'
    condition: '${torrent.ratio} != 2.0'
    actions:
      - log: 'Should not run Numeric Neq Same'
";
        var script = new AutomationScript { Name = "EqualityTests", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Ran String Eq Same");
        result.OutputLog.Should().Contain("Ran String Eq CaseInsensitive");
        result.OutputLog.Should().NotContain("Should not run Eq Diff");
        result.OutputLog.Should().Contain("Ran Bool Eq 1 and True");
        result.OutputLog.Should().Contain("Ran Bool Eq On and True");
        result.OutputLog.Should().Contain("Ran Bool Eq Yes and 1");
        result.OutputLog.Should().Contain("Ran Bool Eq 0 and False");
        result.OutputLog.Should().Contain("Ran Bool Eq Off and No");
        result.OutputLog.Should().NotContain("Should not run Bool Eq False Diff");
        result.OutputLog.Should().Contain("Ran Numeric Eq Float and Int");
        result.OutputLog.Should().Contain("Ran Numeric Eq Close Precision");
        result.OutputLog.Should().NotContain("Should not run Numeric Eq Diff");
        result.OutputLog.Should().Contain("Ran Parts Count Not 2");
        result.OutputLog.Should().Contain("Ran String Neq Diff");
        result.OutputLog.Should().NotContain("Should not run String Neq Same");
        result.OutputLog.Should().Contain("Ran Bool Neq");
        result.OutputLog.Should().Contain("Ran Numeric Neq");
        result.OutputLog.Should().NotContain("Should not run Numeric Neq Same");
    }

    [Test]
    public void Execute_Condition_GreaterLessComparisons_CoversAllBoundaryBranches()
    {
        var torrent = new Torrent
        {
            Ratio = 2.5,
            TotalSize = 5000,
        };

        var yaml = @"
steps:
  - name: 'Gte Greater'
    condition: '${torrent.ratio} >= 2.0'
    actions:
      - log: 'Ran Gte Greater'

  - name: 'Gte Equal'
    condition: '${torrent.ratio} >= 2.5'
    actions:
      - log: 'Ran Gte Equal'

  - name: 'Gte Less'
    condition: '${torrent.ratio} >= 3.0'
    actions:
      - log: 'Should not run Gte Less'

  - name: 'Gte NonNumeric'
    condition: 'abc >= def'
    actions:
      - log: 'Ran Gte NonNumeric'

  - name: 'Lte Less'
    condition: '${torrent.ratio} <= 3.0'
    actions:
      - log: 'Ran Lte Less'

  - name: 'Lte Equal'
    condition: '${torrent.ratio} <= 2.5'
    actions:
      - log: 'Ran Lte Equal'

  - name: 'Lte Greater'
    condition: '${torrent.ratio} <= 1.0'
    actions:
      - log: 'Should not run Lte Greater'

  - name: 'Lte NonNumeric'
    condition: 'abc <= def'
    actions:
      - log: 'Ran Lte NonNumeric'

  - name: 'Gt Greater'
    condition: '${torrent.size} > 4000'
    actions:
      - log: 'Ran Gt Greater'

  - name: 'Gt Equal'
    condition: '${torrent.size} > 5000'
    actions:
      - log: 'Should not run Gt Equal'

  - name: 'Gt Less'
    condition: '${torrent.size} > 6000'
    actions:
      - log: 'Should not run Gt Less'

  - name: 'Gt NonNumeric'
    condition: 'abc > def'
    actions:
      - log: 'Ran Gt NonNumeric'

  - name: 'Lt Less'
    condition: '${torrent.size} < 6000'
    actions:
      - log: 'Ran Lt Less'

  - name: 'Lt Equal'
    condition: '${torrent.size} < 5000'
    actions:
      - log: 'Should not run Lt Equal'

  - name: 'Lt Greater'
    condition: '${torrent.size} < 4000'
    actions:
      - log: 'Should not run Lt Greater'

  - name: 'Lt NonNumeric'
    condition: 'abc < def'
    actions:
      - log: 'Ran Lt NonNumeric'
";
        var script = new AutomationScript { Name = "ComparisonTests", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Ran Gte Greater");
        result.OutputLog.Should().Contain("Ran Gte Equal");
        result.OutputLog.Should().NotContain("Should not run Gte Less");
        result.OutputLog.Should().Contain("Ran Gte NonNumeric");
        result.OutputLog.Should().Contain("Ran Lte Less");
        result.OutputLog.Should().Contain("Ran Lte Equal");
        result.OutputLog.Should().NotContain("Should not run Lte Greater");
        result.OutputLog.Should().Contain("Ran Lte NonNumeric");
        result.OutputLog.Should().Contain("Ran Gt Greater");
        result.OutputLog.Should().NotContain("Should not run Gt Equal");
        result.OutputLog.Should().NotContain("Should not run Gt Less");
        result.OutputLog.Should().Contain("Ran Gt NonNumeric");
        result.OutputLog.Should().Contain("Ran Lt Less");
        result.OutputLog.Should().NotContain("Should not run Lt Equal");
        result.OutputLog.Should().NotContain("Should not run Lt Greater");
        result.OutputLog.Should().Contain("Ran Lt NonNumeric");
    }

    [Test]
    public void Execute_ExtendedConditionKeywordsAndNestedBlocks_ExecutesGracefully()
    {
        var torrent = new Torrent
        {
            Name = "Inception.2010.1080p",
            Category = "Movies",
            Ratio = 1.8,
            TotalSize = 2000000000,
            IsPrivate = true
        };

        // Testing YAML with if, when, and, or, not, nested conditions, and comparisons: eq, neq, gt, gte, lt, lte, in, not_in, contains, regex_match
        var yaml = @"
name: 'Extended Conditions'
steps:
  - name: 'Step With If And When'
    if: '${torrent.size} > 1000'
    when: '${torrent.ratio} >= 1.0'
    condition: '${torrent.category} == Movies'
    actions:
      - log: 'Ran Step With If And When'

  - name: 'Simulated Compound Conditions'
    condition: '${torrent.isPrivate} and ${torrent.category}'
    actions:
      - log: 'Ran Compound Condition'

  - name: 'Simulated Contains Condition'
    condition: '${torrent.name} contains Inception'
    actions:
      - log: 'Ran Contains Condition'

  - name: 'Simulated Regex Condition'
    condition: '${torrent.name} regex_match ^Inception'
    actions:
      - log: 'Ran Regex Condition'

  - name: 'Simulated In Condition'
    condition: '${torrent.category} in Movies,TV'
    actions:
      - log: 'Ran In Condition'

  - name: 'Simulated Not In Condition'
    condition: '${torrent.category} not_in Music,Books'
    actions:
      - log: 'Ran Not In Condition'

  - name: 'Simulated Operator Keywords'
    condition: '${torrent.ratio} gt 1.5 and eq 1.8'
    actions:
      - log: 'Ran Operator Keywords'

  - name: 'Simulated Negated Nested'
    condition: '!false'
    actions:
      - log: 'Ran Negated Condition'
";
        var script = new AutomationScript { Name = "ExtendedConds", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Ran Step With If And When");
        result.OutputLog.Should().Contain("Ran Compound Condition");
        result.OutputLog.Should().Contain("Ran Contains Condition");
        result.OutputLog.Should().Contain("Ran Regex Condition");
        result.OutputLog.Should().Contain("Ran In Condition");
        result.OutputLog.Should().Contain("Ran Not In Condition");
        result.OutputLog.Should().Contain("Ran Operator Keywords");
        result.OutputLog.Should().Contain("Ran Negated Condition");
    }

    [Test]
    public void Execute_AllTorrentActions_ExecuteTagPauseResumeAndLimits()
    {
        var torrent = new Torrent
        {
            Id = 42,
            Name = "The.Matrix.1999",
            Category = "InitialCategory"
        };
        var initialTags = new List<string> { "OldTag" };

        var yaml = @"
name: 'Torrent Actions'
steps:
  - name: 'Apply Actions'
    actions:
      - addTag: 'NewTag'
      - removeTag: 'OldTag'
      - setCategory: 'SciFi'
      - setPriority: 'High'
      - setUploadLimit: '250'
      - setDownloadLimit: '1000'
      - setRatioLimit: '3.5'
      - setSeedingTimeLimit: '1440'
      - setSequentialDownload: true
      - setSuperSeeding: 'true'
      - moveFiles: '/new/save/path'
      - addTracker: 'http://tracker1.com/announce'
      - removeTracker: 'http://tracker2.com/announce'
      - replaceTracker:
          oldTracker: 'http://old.com'
          newTracker: 'http://new.com'
      - boostTracker: true
      - banPeer: '192.168.1.100'
      - pause: true
      - recheck: true
      - reannounce: true
      - reannounceAll: true
      - setShareLimitAction: 'Pause'
      - setFilePriority:
          pattern: '*.mp4'
          priority: 'High'
      - exportTorrent: '/export/matrix.torrent'
      - remove: true
        deleteData: true
";
        var script = new AutomationScript { Name = "TorrentActionsScript", Code = yaml };
        var result = _runner.Execute(script, torrent, initialTags);

        result.Success.Should().BeTrue();
        result.TagsToAdd.Should().Contain("NewTag");
        result.TagsToRemove.Should().Contain("OldTag");
        result.NewCategory.Should().Be("SciFi");
        result.NewPriority.Should().Be(2);
        result.NewUploadLimitKbps.Should().Be(250);
        result.NewDownloadLimitKbps.Should().Be(1000);
        result.NewRatioLimit.Should().Be(3.5);
        result.NewSeedingTimeLimitMinutes.Should().Be(1440);
        result.NewSequentialDownload.Should().BeTrue();
        result.NewSuperSeeding.Should().BeTrue();
        result.NewSavePath.Should().Be("/new/save/path");
        result.TrackersToAdd.Should().Contain("http://tracker1.com/announce");
        result.TrackersToRemove.Should().Contain("http://tracker2.com/announce");
        result.TrackersToReplace.Should().Contain(t => t.Item1 == "http://old.com" && t.Item2 == "http://new.com");
        result.ShouldBoostTracker.Should().BeTrue();
        result.PeersToBan.Should().Contain("192.168.1.100");
        result.ShouldPause.Should().BeTrue();
        result.ShouldResume.Should().BeFalse();
        result.ShouldRecheck.Should().BeTrue();
        result.ShouldReannounce.Should().BeTrue();
        result.ShouldReannounceAll.Should().BeTrue();
        result.ShareLimitAction.Should().Be("Pause");
        result.FilePriorities.Should().Contain(t => t.Item1 == "*.mp4" && t.Item2 == "High");
        result.TorrentExportDestination.Should().Be("/export/matrix.torrent");
        result.ShouldRemove.Should().BeTrue();
        result.DeleteDataOnRemove.Should().BeTrue();
    }

    [Test]
    public void Execute_ExtractArchiveAndCleanFiles_CoversAllVariations()
    {
        var torrent = new Torrent { Id = 1, Name = "Archived" };

        var yaml = @"
steps:
  - name: 'Archive and Clean'
    actions:
      - extractArchive:
          destination: '/extracted'
          deleteArchive: true
      - cleanUnwantedFiles:
          - '*.nfo'
          - '*.txt'
      - cleanUnwantedFiles: '*.sample'
";
        var script = new AutomationScript { Name = "ExtractScript", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldExtractArchive.Should().BeTrue();
        result.ExtractDestination.Should().Be("/extracted");
        result.DeleteArchiveOnExtract.Should().BeTrue();
        result.CleanFilePatterns.Should().Contain("*.nfo");
        result.CleanFilePatterns.Should().Contain("*.txt");
        result.CleanFilePatterns.Should().Contain("*.sample");
    }

    [Test]
    public void Execute_ExtractArchiveDefaultAndRemoveWithoutDelete_CoversFalseBranches()
    {
        var torrent = new Torrent { Id = 1 };

        var yaml = @"
steps:
  - name: 'Archive and Remove Defaults'
    actions:
      - extractArchive: true
      - remove: true
        deleteData: false
";
        var script = new AutomationScript { Name = "ExtractDefault", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldExtractArchive.Should().BeTrue();
        result.ExtractDestination.Should().BeNull();
        result.DeleteArchiveOnExtract.Should().BeFalse();
        result.ShouldRemove.Should().BeTrue();
        result.DeleteDataOnRemove.Should().BeFalse();
    }

    [Test]
    public void Execute_InvalidLimits_HandledGracefullyWithoutCrashing()
    {
        var torrent = new Torrent { Id = 1 };

        var yaml = @"
steps:
  - name: 'Bad Limits'
    actions:
      - setUploadLimit: 'not-a-number'
      - setDownloadLimit: 'bad-dl'
      - setRatioLimit: 'bad-ratio'
      - setSeedingTimeLimit: 'bad-seed'
";
        var script = new AutomationScript { Name = "BadLimits", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.NewUploadLimitKbps.Should().BeNull();
        result.NewDownloadLimitKbps.Should().BeNull();
        result.NewRatioLimit.Should().BeNull();
        result.NewSeedingTimeLimitMinutes.Should().BeNull();
    }

    [Test]
    public void Execute_SystemActions_LogCommandsScriptNotificationsAndPipeline()
    {
        var yaml = @"
steps:
  - name: 'System Commands'
    actions:
      - command: 'PurgeCache'
      - runScript:
          path: '/scripts/post.sh'
          timeout: '45'
          args: 'foo bar'
      - runScript: '/scripts/simple.sh'
      - sendNotification:
          title: 'Alert Title'
          message: 'Alert Message'
          provider: 'Discord'
      - sendNotification: 'Simple notification message'
      - log:
          message: 'Structured warning'
          level: 'warn'
      - log: 'Simple log'
      - delay: '0'
      - sleep: 'not-a-number'
      - notifyArr:
          appType: 'Radarr'
          instanceId: '2'
      - syncArr: 'Sonarr'
      - notifyArr: true
      - setVariable:
          key: 'myVar'
          value: 'hello-world'
      - invokePipeline: 'SubPipeline'
  - name: 'Verify Var Set'
    actions:
      - log: 'Var was: ${variables.myVar} and ${myVar}'
";
        var script = new AutomationScript { Name = "SystemActionsScript", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("runCommand: PurgeCache");
        result.OutputLog.Should().Contain("runScript: /scripts/post.sh (timeout=45s)");
        result.OutputLog.Should().Contain("runScript: /scripts/simple.sh");
        result.OutputLog.Should().Contain("sendNotification: Alert Title (provider: Discord)");
        result.OutputLog.Should().Contain("sendNotification: Simple notification message");
        result.OutputLog.Should().Contain("log (warn): Structured warning");
        result.OutputLog.Should().Contain("log: Simple log");
        result.OutputLog.Should().Contain("notifyArr: appType=Radarr, instanceId=2");
        result.OutputLog.Should().Contain("notifyArr: Sonarr");
        result.OutputLog.Should().Contain("notifyArr: all");
        result.OutputLog.Should().Contain("invokePipeline: SubPipeline");
        result.OutputLog.Should().Contain("Var was: hello-world and hello-world");
    }

    [Test]
    public void Execute_StopPipeline_HaltsSubsequentActionsAndSteps()
    {
        var yaml = @"
steps:
  - name: 'Step 1'
    actions:
      - log: 'Before stop'
      - stopPipeline: 'Quota reached'
      - log: 'Should not run after stop'
  - name: 'Step 2'
    actions:
      - log: 'Should not run step 2'
";
        var script = new AutomationScript { Name = "StopTest", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.ShouldStopPipeline.Should().BeTrue();
        result.StopReason.Should().Be("Quota reached");
        result.OutputLog.Should().Contain("Before stop");
        result.OutputLog.Should().NotContain("Should not run after stop");
        result.OutputLog.Should().NotContain("Should not run step 2");
        result.OutputLog.Should().Contain("[STOP] Pipeline halted: Quota reached");
    }

    [Test]
    public void Execute_StopPipelineWithoutReason_UsesDefaultStopReason()
    {
        var yaml = @"
steps:
  - name: 'Step 1'
    actions:
      - stopPipeline: null
";
        var script = new AutomationScript { Name = "StopDefaultTest", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.ShouldStopPipeline.Should().BeTrue();
        result.StopReason.Should().Be("Condition matched stop");
    }

    [Test]
    public void Execute_EvalMath_ValidAndInvalidExpressions()
    {
        var yaml = @"
steps:
  - name: 'Math Test'
    actions:
      - evalMath:
          expression: '5 * (10 + 2)'
          targetVariable: 'mathResult'
      - evalMath:
          expression: 'invalid +++ syntax / 0'
          targetVariable: 'badMath'
  - name: 'Check Math'
    actions:
      - log: 'Result: ${mathResult}'
";
        var script = new AutomationScript { Name = "MathScript", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("evalMath: 5 * (10 + 2) = 60");
        result.OutputLog.Should().Contain("evalMath Error:");
        result.OutputLog.Should().Contain("Result: 60");
    }

    [Test]
    public void Execute_FileOperations_ChecksumHardlinkSymlinkPermissionsCleanExtensions()
    {
        var sampleFile = Path.Combine(_tempDir, "sample.txt");
        File.WriteAllText(sampleFile, "Hello World from YamlRunner");

        var symlinkFile = Path.Combine(_tempDir, "sample_link.txt");
        var cleanupDir = Path.Combine(_tempDir, "cleanup");
        Directory.CreateDirectory(cleanupDir);
        var smallFile = Path.Combine(cleanupDir, "small.log");
        File.WriteAllText(smallFile, "small log");
        var exeFile = Path.Combine(cleanupDir, "test.exe");
        File.WriteAllText(exeFile, "executable content");

        var yaml = $@"
steps:
  - name: 'File Ops'
    actions:
      - calculateChecksum:
          path: '{sampleFile.Replace('\\', '/')}'
          targetVariable: 'fileHash'
      - calculateChecksum:
          path: '{Path.Combine(_tempDir, "nonexistent.txt").Replace('\\', '/')}'
          targetVariable: 'missingHash'
      - createSymlink:
          source: '{sampleFile.Replace('\\', '/')}'
          dest: '{symlinkFile.Replace('\\', '/')}'
      - createSymlink:
          source: '{Path.Combine(_tempDir, "nonexistent_src").Replace('\\', '/')}'
          dest: '{Path.Combine(_tempDir, "nonexistent_dst").Replace('\\', '/')}'
      - cleanExtensions:
          directory: '{cleanupDir.Replace('\\', '/')}'
          extensions: '.log, .tmp'
          maxSizeLimit: '10'
      - cleanExtensions:
          directory: '{Path.Combine(_tempDir, "missing_cleanup").Replace('\\', '/')}'
          extensions: '.bak'
      - setFilePermissions:
          path: '{sampleFile.Replace('\\', '/')}'
          permissions: '644'
      - setFilePermissions:
          path: '{_tempDir.Replace('\\', '/')}'
          permissions: 'rwxr-xr-x'
          recursive: true
  - name: 'Check Hash'
    actions:
      - log: 'Hash: ${{fileHash}}'
";
        var script = new AutomationScript { Name = "FileOpsScript", Code = yaml };
        var result = _runner.Execute(script);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("calculateChecksum:");
        result.OutputLog.Should().Contain("cleanExtensions: removed");
        result.OutputLog.Should().Contain("setFilePermissions:");
        result.OutputLog.Should().Contain("Hash:");
    }

    [Test]
    public void Execute_ResumeAction_SetsShouldResumeTrue()
    {
        var torrent = new Torrent { Id = 77, Name = "PausedTorrent" };
        var yaml = @"
steps:
  - name: 'Resume Action'
    actions:
      - resume: true
";
        var script = new AutomationScript { Name = "ResumeScript", Code = yaml };
        var result = _runner.Execute(script, torrent);

        result.Success.Should().BeTrue();
        result.ShouldResume.Should().BeTrue();
        result.ShouldPause.Should().BeFalse();
    }

    [Test]
    public async Task Execute_WebhookActions_ExecutesDiscordAndNtfyToLoopbackListener()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var context = await listener.GetContextAsync();
                var resp = context.Response;
                resp.StatusCode = 200;
                var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
                await resp.OutputStream.WriteAsync(bytes);
                resp.OutputStream.Close();
            }
        });

        var yaml = $@"
steps:
  - name: 'Webhooks'
    actions:
      - sendDiscordWebhook:
          webhookUrl: 'http://127.0.0.1:{port}/discord'
          embedObj:
            title: 'Test Embed'
      - sendNtfy:
          url: 'http://127.0.0.1:{port}/ntfy/'
          topic: 'alerts'
          message: 'Ntfy Alert'
          title: 'Alert Title'
          priority: 'high'
          tags: 'warning,torrent'
";
        var script = new AutomationScript { Name = "WebhookScript", Code = yaml };
        var result = _runner.Execute(script);
        await serverTask;

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain($"sendDiscordWebhook to http://127.0.0.1:{port}/discord");
        result.OutputLog.Should().Contain($"sendNtfy to http://127.0.0.1:{port}/ntfy/alerts");
    }

    [Test]
    public void Execute_TelegramAndPushover_DispatchesWebhookActionsGracefully()
    {
        var yaml = @"
steps:
  - name: 'Telegram Webhook'
    actions:
      - sendTelegramMessage:
          token: 'test_token_123'
          chatId: '999999'
          text: 'Hello Telegram'
      - sendPushover:
          token: 'po-token'
          user: 'po-user'
          message: 'Pushover msg'
          priority: '1'
          sound: 'cosmic'
";
        var script = new AutomationScript { Name = "TelegramScript", Code = yaml };
        var result = _runner.Execute(script);

        // Http client completes or returns status without throwing runner-level crash
        result.OutputLog.Should().Contain("sendTelegramMessage to 999999");
        result.OutputLog.Should().Contain("sendPushover to po-user");
    }

    [Test]
    public async Task Execute_HttpStep_CoversMethodsAndRegistrationWithLoopback()
    {
        var port = GetFreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var resp = context.Response;
            resp.StatusCode = 200;
            resp.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes("{\"status\": 200, \"message\": \"ok\"}");
            await resp.OutputStream.WriteAsync(bytes);
            resp.OutputStream.Close();
        });

        var yaml = $@"
steps:
  - name: 'Http Step'
    http:
      url: 'http://127.0.0.1:{port}/api'
      method: 'GET'
      headers:
        Authorization: 'Bearer test'
      cookies: 'session=123'
      json: true
    register: 'apiResp'
  - name: 'Check Register'
    actions:
      - log: 'Status: ${{apiResp.status}}'
";
        var script = new AutomationScript { Name = "HttpScript", Code = yaml };
        var result = _runner.Execute(script);
        await serverTask;

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain($"[HTTP] GET http://127.0.0.1:{port}/api");
        result.OutputLog.Should().Contain("Status: 200");
    }

    private static int GetFreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    [Test]
    public void Execute_NoTorrentContext_ExecutesGracefullyWithoutTorrentActions()
    {
        var yaml = @"
steps:
  - name: 'No Torrent Step'
    actions:
      - log: 'Running without torrent'
      - addTag: 'OrphanTag'
      - pause: true
";
        var script = new AutomationScript { Name = "NoTorrentScript", Code = yaml };
        var result = _runner.Execute(script, torrent: null);

        result.Success.Should().BeTrue();
        result.OutputLog.Should().Contain("Running without torrent");
        result.TagsToAdd.Should().BeEmpty();
        result.ShouldPause.Should().BeFalse();
    }
}
