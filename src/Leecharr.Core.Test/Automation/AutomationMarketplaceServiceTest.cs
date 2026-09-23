// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Automation;

namespace Leecharr.Core.Test.Automation;

[TestFixture]
public class AutomationMarketplaceServiceTest
{
    private AutomationMarketplaceService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.service = new AutomationMarketplaceService();
    }

    [Test]
    public void GetTemplates_ReturnsPredefinedBuiltinTemplates()
    {
        var templates = this.service.GetTemplates();

        templates.Should().NotBeNull();
        templates.Should().HaveCountGreaterOrEqualTo(5);

        var ids = templates.Select(t => t.Id).ToList();
        ids.Should().Contain(new[]
        {
            "pt-auto-zap-tokens",
            "auto-tag-by-ratio-pause",
            "discord-download-webhook",
            "yaml-auto-categorize-media",
            "stalled-torrent-pruner",
        });
    }

    [TestCase("pt-auto-zap-tokens", "Private Tracker Auto-Zap & Token Spender", "Private Trackers", AutomationTrigger.TorrentAdded, AutomationLanguage.JavaScript)]
    [TestCase("auto-tag-by-ratio-pause", "Seed Ratio Goal & Auto-Pause", "Ratio Management", AutomationTrigger.RatioReached, AutomationLanguage.JavaScript)]
    [TestCase("discord-download-webhook", "Discord Webhook Notification on Complete", "Notifications", AutomationTrigger.TorrentCompleted, AutomationLanguage.JavaScript)]
    [TestCase("yaml-auto-categorize-media", "Declarative Media Categorizer (YAML)", "Organization", AutomationTrigger.TorrentAdded, AutomationLanguage.Yaml)]
    [TestCase("stalled-torrent-pruner", "Stalled Torrent Pruner & Cleaner", "Maintenance", AutomationTrigger.Scheduled, AutomationLanguage.JavaScript)]
    public void GetTemplates_ContainsExpectedMetadataForEachTemplate(
        string id,
        string expectedName,
        string expectedCategory,
        AutomationTrigger expectedTrigger,
        AutomationLanguage expectedLanguage)
    {
        var template = this.service.GetTemplates().FirstOrDefault(t => t.Id == id);

        template.Should().NotBeNull();
        template!.Name.Should().Be(expectedName);
        template.Category.Should().Be(expectedCategory);
        template.Trigger.Should().Be(expectedTrigger);
        template.Language.Should().Be(expectedLanguage);
        template.Author.Should().Be("Seedarr Community");
        template.Version.Should().NotBeNullOrWhiteSpace();
        template.Description.Should().NotBeNullOrWhiteSpace();
        template.Code.Should().NotBeNullOrWhiteSpace();
    }

    [TestCase("pt-auto-zap-tokens", "PT-AUTO-ZAP-TOKENS")]
    [TestCase("pt-auto-zap-tokens", "Pt-Auto-Zap-Tokens")]
    [TestCase("discord-download-webhook", "DISCORD-DOWNLOAD-WEBHOOK")]
    public void GetTemplate_IsCaseInsensitive(string templateId, string lookupId)
    {
        var template = this.service.GetTemplate(lookupId);

        template.Should().NotBeNull();
        template!.Id.Should().Be(templateId);
    }

    [TestCase("non-existent-template")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void GetTemplate_WhenNotFoundOrInvalid_ReturnsNull(string templateId)
    {
        var template = this.service.GetTemplate(templateId);

        template.Should().BeNull();
    }

    [Test]
    public void GetTemplates_SupportsFilteringByCategory()
    {
        var templates = this.service.GetTemplates();

        var notificationTemplates = templates.Where(t => t.Category == "Notifications").ToList();
        notificationTemplates.Should().ContainSingle();
        notificationTemplates[0].Id.Should().Be("discord-download-webhook");

        var ratioTemplates = templates.Where(t => t.Category == "Ratio Management").ToList();
        ratioTemplates.Should().ContainSingle();
        ratioTemplates[0].Id.Should().Be("auto-tag-by-ratio-pause");
    }

    [Test]
    public void GetTemplates_SupportsSearchingByKeyword()
    {
        var templates = this.service.GetTemplates();

        var discordResults = templates.Where(t =>
            t.Name.Contains("Discord", StringComparison.OrdinalIgnoreCase) ||
            t.Description.Contains("Discord", StringComparison.OrdinalIgnoreCase)).ToList();

        discordResults.Should().ContainSingle();
        discordResults[0].Id.Should().Be("discord-download-webhook");
    }

    [Test]
    public void InstallTemplate_WhenTemplateDoesNotExist_ThrowsInvalidOperationException()
    {
        var act = () => this.service.InstallTemplate("unknown-id-999");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown-id-999*");
    }

    [Test]
    public void InstallTemplate_WithDefaultParameters_InstallsWithTemplateDefaults()
    {
        var script = this.service.InstallTemplate("auto-tag-by-ratio-pause");

        script.Should().NotBeNull();
        script.Name.Should().Be("Seed Ratio Goal & Auto-Pause");
        script.Description.Should().Be("Monitors torrent ratio; when target seed ratio is satisfied, tags torrent as 'ratio-met' and pauses seeding.");
        script.Trigger.Should().Be(AutomationTrigger.RatioReached);
        script.Language.Should().Be(AutomationLanguage.JavaScript);
        script.IsEnabled.Should().BeTrue();
        script.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        script.InputsJson.Should().NotBeNullOrWhiteSpace();
        var inputs = JsonSerializer.Deserialize<Dictionary<string, string>>(script.InputsJson);
        inputs.Should().NotBeNull();
        inputs!["target_ratio"].Should().Be("3.0");
        inputs["tag_name"].Should().Be("ratio-met");
    }

    [Test]
    public void InstallTemplate_WithCustomNameAndInputs_OverridesDefaultsAndMergesInputs()
    {
        var customInputs = new Dictionary<string, string>
        {
            { "tracker_domain", "private.tracker.org" },
            { "session_cookie", "secret-session-token-xyz" },
            { "custom_setting", "extra_val" },
        };

        var script = this.service.InstallTemplate(
            "pt-auto-zap-tokens",
            customName: "My Custom Zap Automation",
            customInputs: customInputs);

        script.Should().NotBeNull();
        script.Name.Should().Be("My Custom Zap Automation");
        script.Trigger.Should().Be(AutomationTrigger.TorrentAdded);
        script.Language.Should().Be(AutomationLanguage.JavaScript);
        script.IsEnabled.Should().BeTrue();

        var inputs = JsonSerializer.Deserialize<Dictionary<string, string>>(script.InputsJson);
        inputs.Should().NotBeNull();
        inputs!["tracker_domain"].Should().Be("private.tracker.org");
        inputs["session_cookie"].Should().Be("secret-session-token-xyz");
        inputs["zap_tag"].Should().Be("zapped"); // Kept template default
        inputs["custom_setting"].Should().Be("extra_val"); // Added custom key
    }

    [Test]
    public void InstallTemplate_ForTemplateWithNoInputFields_ProducesEmptyInputsJson()
    {
        var script = this.service.InstallTemplate("yaml-auto-categorize-media");

        script.Should().NotBeNull();
        script.Name.Should().Be("Declarative Media Categorizer (YAML)");
        script.Language.Should().Be(AutomationLanguage.Yaml);

        var inputs = JsonSerializer.Deserialize<Dictionary<string, string>>(script.InputsJson);
        inputs.Should().NotBeNull();
        inputs.Should().BeEmpty();
    }
}
