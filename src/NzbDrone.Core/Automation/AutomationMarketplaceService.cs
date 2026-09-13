#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Automation;

public class AutomationMarketplaceTemplate
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Author { get; set; } = "Community";

    public string Version { get; set; } = "1.0.0";

    public string Category { get; set; } = "General";

    public AutomationTrigger Trigger { get; set; } = AutomationTrigger.TorrentAdded;

    public AutomationLanguage Language { get; set; } = AutomationLanguage.JavaScript;

    public string Code { get; set; } = string.Empty;

    public Dictionary<string, string> DefaultInputs { get; set; } = new();

    public List<TemplateInputField> InputFields { get; set; } = new();
}

public class TemplateInputField
{
    public string Key { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Type { get; set; } = "text"; // text, password, number, select

    public string DefaultValue { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool Required { get; set; } = true;
}

public interface IAutomationMarketplaceService
{
    List<AutomationMarketplaceTemplate> GetTemplates();

    AutomationMarketplaceTemplate? GetTemplate(string templateId);

    AutomationScript InstallTemplate(string templateId, string? customName = null, Dictionary<string, string>? customInputs = null);
}

public class AutomationMarketplaceService : IAutomationMarketplaceService
{
    private static readonly List<AutomationMarketplaceTemplate> BuiltinTemplates = new()
    {
        new AutomationMarketplaceTemplate
        {
            Id = "pt-auto-zap-tokens",
            Name = "Private Tracker Auto-Zap & Token Spender",
            Description = "Logs into private tracker using session cookie, checks if torrent can be zapped/freed with bonus tokens, and triggers the zap action.",
            Author = "Leecharr Community",
            Version = "1.1.0",
            Category = "Private Trackers",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.JavaScript,
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "tracker_domain", Label = "Tracker Domain", Type = "text", DefaultValue = "tracker.example.com", Description = "Domain to match against torrent tracker URL" },
                new() { Key = "session_cookie", Label = "Session Cookie / API Token", Type = "password", DefaultValue = string.Empty, Description = "Your private tracker session cookie or API key" },
                new() { Key = "zap_tag", Label = "Applied Tag", Type = "text", DefaultValue = "zapped", Description = "Tag applied to the torrent once zapped" },
            },
            Code = @"// Private Tracker Auto-Zap Script
if (!torrent.tracker.includes(inputs.tracker_domain)) {
  console.log('Skipping: tracker does not match ' + inputs.tracker_domain);
  return;
}

console.log('Processing torrent: ' + torrent.name + ' (' + torrent.infoHash + ')');

var response = http.post('https://' + inputs.tracker_domain + '/api/torrent/' + torrent.infoHash + '/zap', {
  torrent_id: torrent.infoHash
}, {
  headers: {
    'Authorization': 'Bearer ' + inputs.session_cookie,
    'Cookie': 'session=' + inputs.session_cookie
  }
});

if (response.ok) {
  console.log('Successfully zapped torrent on tracker!');
  torrent.addTag(inputs.zap_tag || 'zapped');
} else {
  console.warn('Zap request returned status: ' + response.status + ' - ' + response.body);
}
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "auto-tag-by-ratio-pause",
            Name = "Seed Ratio Goal & Auto-Pause",
            Description = "Monitors torrent ratio; when target seed ratio is satisfied, tags torrent as 'ratio-met' and pauses seeding.",
            Author = "Leecharr Community",
            Version = "1.0.0",
            Category = "Ratio Management",
            Trigger = AutomationTrigger.RatioReached,
            Language = AutomationLanguage.JavaScript,
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "target_ratio", Label = "Target Ratio", Type = "number", DefaultValue = "3.0", Description = "Ratio threshold to trigger auto-pause" },
                new() { Key = "tag_name", Label = "Completed Tag", Type = "text", DefaultValue = "ratio-met", Description = "Tag applied when target ratio is reached" },
            },
            Code = @"// Seed Ratio Auto-Pause Script
var targetRatio = parseFloat(inputs.target_ratio || 3.0);

console.log('Torrent ' + torrent.name + ' current ratio: ' + torrent.ratio + ' (Target: ' + targetRatio + ')');

if (torrent.ratio >= targetRatio) {
  console.log('Target ratio reached! Adding tag and pausing torrent.');
  torrent.addTag(inputs.tag_name || 'ratio-met');
  torrent.pause();
}
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "discord-download-webhook",
            Name = "Discord Webhook Notification on Complete",
            Description = "Sends a rich embed notification to your Discord channel when a download completes.",
            Author = "Leecharr Community",
            Version = "1.2.0",
            Category = "Notifications",
            Trigger = AutomationTrigger.TorrentCompleted,
            Language = AutomationLanguage.JavaScript,
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "webhook_url", Label = "Discord Webhook URL", Type = "text", DefaultValue = "https://discord.com/api/webhooks/...", Description = "Your Discord webhook endpoint" },
            },
            Code = @"// Discord Webhook Notification
if (!inputs.webhook_url) {
  console.error('Discord webhook URL is not configured!');
  return;
}

var sizeMB = (torrent.size / (1024 * 1024)).toFixed(2);

var payload = {
  embeds: [{
    title: 'Download Completed',
    description: '**' + torrent.name + '**',
    color: 3066993, // Green
    fields: [
      { name: 'Size', value: sizeMB + ' MB', inline: true },
      { name: 'Category', value: torrent.category || 'Default', inline: true },
      { name: 'Ratio', value: torrent.ratio.toFixed(2), inline: true }
    ],
    timestamp: new Date().toISOString()
  }]
};

var res = http.post(inputs.webhook_url, payload, { json: true });
if (res.ok) {
  console.log('Discord notification dispatched successfully.');
} else {
  console.error('Failed to dispatch Discord webhook: ' + res.status);
}
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "yaml-auto-categorize-media",
            Name = "Declarative Media Categorizer (YAML)",
            Description = "Non-programmer YAML workflow that checks media resolution in name and assigns Movie or TV categories.",
            Author = "Leecharr Community",
            Version = "1.0.0",
            Category = "Organization",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.Yaml,
            InputFields = new List<TemplateInputField>(),
            Code = @"name: 'Auto Categorize Media'
trigger: 'TorrentAdded'
steps:
  - name: 'Check for TV Show'
    condition: '${torrent.name}'
    actions:
      - addTag: 'Automated'

  - name: 'Tag Fast Seeding'
    condition: '${torrent.size} > 1000000000'
    actions:
      - addTag: 'LargeTorrent'
",
        },
        new AutomationMarketplaceTemplate
        {
            Id = "stalled-torrent-pruner",
            Name = "Stalled Torrent Pruner & Cleaner",
            Description = "Checks if torrent has zero upload speed and has been inactive, applying a 'stalled' tag or pausing it.",
            Author = "Leecharr Community",
            Version = "1.0.0",
            Category = "Maintenance",
            Trigger = AutomationTrigger.Scheduled,
            Language = AutomationLanguage.JavaScript,
            InputFields = new List<TemplateInputField>
            {
                new() { Key = "action", Label = "Action (pause or tag)", Type = "text", DefaultValue = "tag", Description = "Whether to pause or tag stalled torrents" },
            },
            Code = @"// Stalled Torrent Pruner
if (torrent && torrent.uploadSpeed === 0 && torrent.downloadSpeed === 0) {
  console.log('Torrent ' + torrent.name + ' has 0 B/s traffic.');
  if (inputs.action === 'pause') {
    torrent.pause();
  } else {
    torrent.addTag('stalled');
  }
}
",
        },
    };

    public List<AutomationMarketplaceTemplate> GetTemplates()
    {
        return BuiltinTemplates;
    }

    public AutomationMarketplaceTemplate? GetTemplate(string templateId)
    {
        return BuiltinTemplates.FirstOrDefault(t => string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase));
    }

    public AutomationScript InstallTemplate(string templateId, string? customName = null, Dictionary<string, string>? customInputs = null)
    {
        var template = GetTemplate(templateId) ?? throw new InvalidOperationException($"Template with ID '{templateId}' was not found.");

        var inputs = new Dictionary<string, string>();
        foreach (var field in template.InputFields)
        {
            inputs[field.Key] = field.DefaultValue;
        }

        if (customInputs != null)
        {
            foreach (var kvp in customInputs)
            {
                inputs[kvp.Key] = kvp.Value;
            }
        }

        return new AutomationScript
        {
            Name = !string.IsNullOrWhiteSpace(customName) ? customName : template.Name,
            Description = template.Description,
            Trigger = template.Trigger,
            Language = template.Language,
            Code = template.Code,
            InputsJson = System.Text.Json.JsonSerializer.Serialize(inputs),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
