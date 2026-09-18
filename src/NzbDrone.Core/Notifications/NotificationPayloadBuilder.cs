// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NzbDrone.Core.MediaEnrichment;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Notifications;

public static class NotificationPayloadBuilder
{
    public static (string ChatId, string Token, string User, string Sound) ExtractProviderSettings(string settings)
    {
        var chatId = string.Empty;
        var token = string.Empty;
        var user = string.Empty;
        var sound = string.Empty;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (chatId, token, user, sound);
        }

        if (settings.TrimStart().StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(settings);
                var root = doc.RootElement;
                if (root.TryGetProperty("chat_id", out var c) || root.TryGetProperty("chatId", out c))
                {
                    chatId = c.GetString() ?? c.ToString();
                }

                if (root.TryGetProperty("token", out var t) || root.TryGetProperty("botToken", out t) || root.TryGetProperty("apiKey", out t) || root.TryGetProperty("appToken", out t))
                {
                    token = t.GetString() ?? t.ToString();
                }

                if (root.TryGetProperty("user", out var u) || root.TryGetProperty("userKey", out u))
                {
                    user = u.GetString() ?? u.ToString();
                }

                if (root.TryGetProperty("sound", out var s) || root.TryGetProperty("Sound", out s))
                {
                    sound = s.GetString() ?? s.ToString();
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(chatId) && settings.Contains("chat_id="))
        {
            var match = Regex.Match(settings, @"chat_id=([^&]+)");
            if (match.Success)
            {
                chatId = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            var match = Regex.Match(settings, @"(?:token|appToken|botToken|apiKey)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                token = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(user) && settings.Contains("user="))
        {
            var match = Regex.Match(settings, @"user=([^&]+)");
            if (match.Success)
            {
                user = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.IsNullOrEmpty(sound) && settings.Contains("sound=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(settings, @"sound=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                sound = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return (chatId, token, user, sound);
    }

    public static string ResolveTargetUrl(string implementation, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return string.Empty;
        }

        var trimmed = settings.Trim();

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var (_, token, _, _) = ExtractProviderSettings(trimmed);
            if (!string.IsNullOrEmpty(token))
            {
                return $"https://api.telegram.org/bot{token}/sendMessage";
            }

            return "https://api.telegram.org/bot/sendMessage";
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            return "https://api.pushover.net/1/messages.json";
        }

        var candidateUrl = trimmed;

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("url", out var u) || root.TryGetProperty("Url", out u) || root.TryGetProperty("webhookUrl", out u) || root.TryGetProperty("serverUrl", out u) || root.TryGetProperty("ServerUrl", out u))
                {
                    candidateUrl = u.GetString() ?? trimmed;
                }
            }
            catch
            {
            }
        }
        else if (trimmed.Contains("url="))
        {
            var match = Regex.Match(trimmed, @"url=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                candidateUrl = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(candidateUrl))
        {
            return AppendEndpointPath(candidateUrl, "notify");
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(candidateUrl))
        {
            return AppendEndpointPath(candidateUrl, "message");
        }

        return candidateUrl;
    }

    public static string ResolveCustomHeaders(string implementation, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return null;
        }

        var trimmed = settings.Trim();
        string explicitHeaders = null;

        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var propertyNames = new[] { "headers", "Headers", "customHeaders", "CustomHeaders", "custom_headers" };
                foreach (var propName in propertyNames)
                {
                    if (root.TryGetProperty(propName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Object)
                        {
                            var raw = prop.GetRawText()?.Trim();
                            explicitHeaders = string.IsNullOrWhiteSpace(raw) || raw == "{}" ? null : raw;
                            break;
                        }

                        if (prop.ValueKind == JsonValueKind.String)
                        {
                            var str = prop.GetString()?.Trim();
                            explicitHeaders = string.IsNullOrWhiteSpace(str) || str == "{}" ? null : str;
                            break;
                        }

                        if (prop.ValueKind == JsonValueKind.Array)
                        {
                            var raw = prop.GetRawText()?.Trim();
                            explicitHeaders = string.IsNullOrWhiteSpace(raw) || raw == "[]" ? null : raw;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        if (explicitHeaders == null)
        {
            var matchKeys = new[] { "headers=", "customHeaders=", "custom_headers=" };
            foreach (var key in matchKeys)
            {
                if (settings.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(settings, $@"{key}([^&]+)", RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var val = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
                        explicitHeaders = string.IsNullOrWhiteSpace(val) || val == "{}" ? null : val;
                        break;
                    }
                }
            }
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            var (_, token, _, _) = ExtractProviderSettings(trimmed);
            if (!string.IsNullOrWhiteSpace(token))
            {
                if (string.IsNullOrWhiteSpace(explicitHeaders))
                {
                    return $"{{\"X-Gotify-Key\":\"{token}\"}}";
                }

                if (explicitHeaders.StartsWith("{"))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(explicitHeaders);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var p in doc.RootElement.EnumerateObject())
                            {
                                dict[p.Name] = p.Value.GetString() ?? p.Value.GetRawText();
                            }

                            if (!dict.ContainsKey("X-Gotify-Key"))
                            {
                                dict["X-Gotify-Key"] = token;
                            }

                            return JsonSerializer.Serialize(dict);
                        }
                    }
                    catch
                    {
                    }
                }

                return $"{explicitHeaders}\nX-Gotify-Key: {token}";
            }
        }

        return explicitHeaders;
    }

    public static object BuildProviderPayload(string implementation, string eventType, Torrent torrent, dynamic meta, object genericPayload, string settings = null)
    {
        var (chatId, token, user, sound) = ExtractProviderSettings(settings);
        var torrentName = torrent?.Name ?? ExtractMessage(genericPayload, eventType);

        if (string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase))
        {
            var title = Truncate($"[{eventType}] {torrentName}", 256);
            var torrentDetails = torrent != null
                ? $"Category: {torrent.Category ?? "None"} | Status: {torrent.Status} | Progress: {torrent.Progress * 100:F1}% | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}");
            var overview = ExtractOverview(meta);
            var rawDesc = !string.IsNullOrWhiteSpace(overview)
                ? $"{torrentDetails}\n\n{overview}"
                : torrentDetails;
            var desc = Truncate(rawDesc, 2048);

            return new
            {
                username = "Leecharr",
                embeds = new object[]
                {
                    new
                    {
                        title,
                        description = desc,
                        color = 16765286, // Gold
                        timestamp = DateTime.UtcNow.ToString("o"),
                    },
                },
            };
        }

        if (string.Equals(implementation, "Slack", StringComparison.OrdinalIgnoreCase))
        {
            var text = torrent != null
                ? $"*Leecharr [{eventType}]* - *{torrent.Name}*\nCategory: {torrent.Category ?? "None"} | Status: {torrent.Status} | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : $"*Leecharr [{eventType}]*\n{ExtractMessage(genericPayload, eventType)}";

            object[] sectionFields;
            if (torrent != null)
            {
                sectionFields = new object[]
                {
                    new { type = "mrkdwn", text = $"*Torrent:*\n{torrent.Name}" },
                    new { type = "mrkdwn", text = $"*Category:*\n{torrent.Category ?? "None"}" },
                    new { type = "mrkdwn", text = $"*Status:*\n{torrent.Status}" },
                    new { type = "mrkdwn", text = $"*Size:*\n{torrent.TotalSize / (1024.0 * 1024.0):F2} MB" },
                };
            }
            else
            {
                sectionFields = new object[]
                {
                    new { type = "mrkdwn", text = $"*Event:*\n{eventType}" },
                    new { type = "mrkdwn", text = $"*Message:*\n{ExtractMessage(genericPayload, eventType)}" },
                };
            }

            var blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = Truncate($"Leecharr [{eventType}]", 150),
                        emoji = true,
                    },
                },
                new
                {
                    type = "section",
                    fields = sectionFields,
                },
                new
                {
                    type = "context",
                    elements = new object[]
                    {
                        new
                        {
                            type = "mrkdwn",
                            text = "Leecharr Notification",
                        },
                    },
                },
            };

            return new
            {
                text = Truncate(text, 3500),
                username = "Leecharr",
                blocks,
            };
        }

        if (string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase))
        {
            var text = torrent != null
                ? $"*Leecharr \\[{EscapeTelegramMarkdown(eventType)}\\]*\n*{EscapeTelegramMarkdown(torrent.Name)}*\nCategory: {EscapeTelegramMarkdown(torrent.Category ?? "None")}\nProgress: {torrent.Progress * 100:F1}%\nStatus: {EscapeTelegramMarkdown(torrent.Status.ToString())}"
                : $"*Leecharr \\[{EscapeTelegramMarkdown(eventType)}\\]*\n{EscapeTelegramMarkdown(ExtractMessage(genericPayload, eventType))}";

            var payloadDict = new Dictionary<string, object>
            {
                ["text"] = TruncateTelegramMarkdown(text, 4096),
                ["parse_mode"] = "Markdown",
            };

            if (!string.IsNullOrEmpty(chatId))
            {
                payloadDict["chat_id"] = chatId;
            }

            return payloadDict;
        }

        if (string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                title = $"Leecharr: {eventType}",
                message = torrent != null ? $"{torrent.Name} ({torrent.Category ?? "Default"}) - {torrent.Status}" : ExtractMessage(genericPayload, eventType),
                priority = ResolveGotifyPriority(eventType),
            };
        }

        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase))
        {
            var rawTitle = $"Leecharr: {eventType}";
            var rawMessage = torrent != null ? $"{torrent.Name} ({torrent.Category ?? "Default"}) - {torrent.Status}" : ExtractMessage(genericPayload, eventType);

            var payloadDict = new Dictionary<string, object>
            {
                ["title"] = Truncate(rawTitle, 250),
                ["message"] = Truncate(rawMessage, 1024),
                ["priority"] = ResolvePushoverPriority(eventType),
            };

            if (!string.IsNullOrEmpty(token))
            {
                payloadDict["token"] = token;
            }

            if (!string.IsNullOrEmpty(user))
            {
                payloadDict["user"] = user;
            }

            if (!string.IsNullOrWhiteSpace(sound))
            {
                payloadDict["sound"] = sound;
            }

            return payloadDict;
        }

        if (string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            var title = $"Leecharr: {eventType}";
            var body = torrent != null
                ? $"Torrent: {torrent.Name}\nCategory: {torrent.Category ?? "None"}\nStatus: {torrent.Status}\nProgress: {torrent.Progress * 100:F1}%\nSize: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}");

            var isWarning = eventType.Contains("HealthIssue", StringComparison.OrdinalIgnoreCase) ||
                            eventType.Contains("Error", StringComparison.OrdinalIgnoreCase) ||
                            eventType.Contains("Failed", StringComparison.OrdinalIgnoreCase);

            return new
            {
                title,
                body,
                type = isWarning ? "warning" : "info",
            };
        }

        return genericPayload;
    }

    internal static string ExtractMessage(object payload, string fallback)
    {
        if (payload == null)
        {
            return fallback;
        }

        if (payload is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        if (payload is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("Message", out var m1) && m1 != null)
            {
                var str1 = m1.ToString();
                if (!string.IsNullOrWhiteSpace(str1))
                {
                    return str1;
                }
            }

            if (dict.TryGetValue("message", out var m2) && m2 != null)
            {
                var str2 = m2.ToString();
                if (!string.IsNullOrWhiteSpace(str2))
                {
                    return str2;
                }
            }
        }

        var prop = payload.GetType().GetProperty("Message", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var value = prop?.GetValue(payload)?.ToString();

        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    internal static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Length > maxLength ? value.Substring(0, maxLength - 3) + "..." : value;
    }

    internal static string ExtractOverview(dynamic meta)
    {
        if (meta == null)
        {
            return null;
        }

        if (meta is TorrentMediaMetadata tmm)
        {
            return tmm.Overview;
        }

        if (meta is IDictionary<string, object> dict && dict.TryGetValue("Overview", out var ovVal))
        {
            return ovVal?.ToString();
        }

        try
        {
            return (string)meta.Overview;
        }
        catch
        {
            try
            {
                var prop = ((object)meta).GetType().GetProperty("Overview");
                if (prop != null)
                {
                    return prop.GetValue((object)meta) as string;
                }
            }
            catch
            {
            }

            return null;
        }
    }

    private static string AppendEndpointPath(string candidateUrl, string endpoint)
    {
        if (Uri.TryCreate(candidateUrl, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri);
            var path = (builder.Path ?? string.Empty).TrimEnd('/');

            if (!path.EndsWith($"/{endpoint}", StringComparison.OrdinalIgnoreCase))
            {
                builder.Path = string.IsNullOrEmpty(path) ? $"/{endpoint}" : $"{path}/{endpoint}";
            }
            else
            {
                builder.Path = path;
            }

            return builder.Uri.AbsoluteUri;
        }

        var clean = candidateUrl.TrimEnd('/');
        return clean.EndsWith($"/{endpoint}", StringComparison.OrdinalIgnoreCase) ? clean : $"{clean}/{endpoint}";
    }

    internal static int ResolveGotifyPriority(string eventType)
    {
        if (string.Equals(eventType, "OnHealthIssue", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnManualInteractionRequired", StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }

        if (string.Equals(eventType, "OnDownloadComplete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnSeedGoalReached", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnExtractComplete", StringComparison.OrdinalIgnoreCase))
        {
            return 5;
        }

        if (string.Equals(eventType, "OnGrab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnMediaInspected", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnTorrentDeleted", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 5;
    }

    internal static int ResolvePushoverPriority(string eventType)
    {
        if (string.Equals(eventType, "OnHealthIssue", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnManualInteractionRequired", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (string.Equals(eventType, "OnDownloadComplete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnSeedGoalReached", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnExtractComplete", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(eventType, "OnGrab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnMediaInspected", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, "OnTorrentDeleted", StringComparison.OrdinalIgnoreCase))
        {
            return -1;
        }

        return 0;
    }

    internal static string EscapeTelegramMarkdown(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            if (c is '_' or '*' or '[' or ']' or '`' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    internal static string TruncateTelegramMarkdown(string text, int maxLength = 4096)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (text.Length <= maxLength)
        {
            var closing = GetClosingTags(text, text.Length);
            var cleanLen = text.Length;

            while (cleanLen > 0 && HasOddTrailingBackslashes(text, cleanLen))
            {
                cleanLen--;
            }

            var baseText = cleanLen == text.Length ? text : text.Substring(0, cleanLen);
            if (string.IsNullOrEmpty(closing))
            {
                return baseText;
            }

            if (baseText.Length + closing.Length <= maxLength)
            {
                return baseText + closing;
            }
        }

        var targetLen = Math.Min(text.Length, maxLength - 3);

        while (targetLen > 0)
        {
            if (HasOddTrailingBackslashes(text, targetLen))
            {
                targetLen--;
                continue;
            }

            var closing = GetClosingTags(text, targetLen);
            if (targetLen + 3 + closing.Length <= maxLength)
            {
                return text.Substring(0, targetLen) + "..." + closing;
            }

            targetLen--;
        }

        return Truncate(text, maxLength);
    }

    internal static string GetClosingTags(string text, int length)
    {
        if (string.IsNullOrEmpty(text) || length <= 0)
        {
            return string.Empty;
        }

        var stack = new List<string>();
        var i = 0;
        var limit = Math.Min(length, text.Length);

        while (i < limit)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (i + 2 < limit && text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`')
            {
                if (stack.Count > 0 && stack[^1] == "```")
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                else
                {
                    stack.Add("```");
                }

                i += 3;
                continue;
            }

            if (text[i] == '`')
            {
                if (stack.Count > 0 && stack[^1] == "```")
                {
                    i++;
                    continue;
                }

                if (stack.Count > 0 && stack[^1] == "`")
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                else
                {
                    stack.Add("`");
                }

                i++;
                continue;
            }

            if (text[i] == '*')
            {
                if (stack.Count > 0 && (stack[^1] == "`" || stack[^1] == "```"))
                {
                    i++;
                    continue;
                }

                if (stack.Count > 0 && stack[^1] == "*")
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                else
                {
                    stack.Add("*");
                }

                i++;
                continue;
            }

            if (text[i] == '_')
            {
                if (stack.Count > 0 && (stack[^1] == "`" || stack[^1] == "```"))
                {
                    i++;
                    continue;
                }

                if (stack.Count > 0 && stack[^1] == "_")
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                else
                {
                    stack.Add("_");
                }

                i++;
                continue;
            }

            i++;
        }

        if (stack.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var idx = stack.Count - 1; idx >= 0; idx--)
        {
            sb.Append(stack[idx]);
        }

        return sb.ToString();
    }

    internal static bool HasOddTrailingBackslashes(string text, int length)
    {
        var count = 0;
        var idx = length - 1;
        while (idx >= 0 && text[idx] == '\\')
        {
            count++;
            idx--;
        }

        return (count % 2) == 1;
    }
}
