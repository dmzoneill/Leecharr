using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
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
            catch (JsonException)
            {
                // Fall back to query-string extraction when JSON parsing fails
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

    public static (string Username, string AvatarUrl) ExtractDiscordSettings(string settings)
    {
        var username = "Leecharr";
        string avatarUrl = null;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (username, avatarUrl);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("username", out var u) ||
                    root.TryGetProperty("Username", out u) ||
                    root.TryGetProperty("user", out u) ||
                    root.TryGetProperty("User", out u))
                {
                    var uVal = u.GetString() ?? u.ToString();
                    if (!string.IsNullOrWhiteSpace(uVal))
                    {
                        username = uVal.Trim();
                    }
                }

                if (root.TryGetProperty("avatarUrl", out var a) ||
                    root.TryGetProperty("avatar_url", out a) ||
                    root.TryGetProperty("AvatarUrl", out a) ||
                    root.TryGetProperty("avatar", out a))
                {
                    var aVal = a.GetString() ?? a.ToString();
                    if (!string.IsNullOrWhiteSpace(aVal))
                    {
                        avatarUrl = aVal.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to query-string extraction when JSON parsing fails
            }
        }
        else
        {
            var uMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:username|Username|user)=([^&]+)", RegexOptions.IgnoreCase);
            if (uMatch.Success)
            {
                var val = Uri.UnescapeDataString(uMatch.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    username = val;
                }
            }

            var aMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:avatarUrl|avatar_url|AvatarUrl|avatar)=([^&]+)", RegexOptions.IgnoreCase);
            if (aMatch.Success)
            {
                var val = Uri.UnescapeDataString(aMatch.Groups[1].Value).Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    avatarUrl = val;
                }
            }
        }

        return (username, avatarUrl);
    }

    public static (string Username, string Password) ExtractBasicAuthSettings(string settings)
    {
        string username = null;
        string password = null;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (username, password);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                var userProps = new[] { "username", "Username", "basicAuthUsername", "BasicAuthUsername" };
                foreach (var prop in userProps)
                {
                    if (root.TryGetProperty(prop, out var u) && u.ValueKind == JsonValueKind.String)
                    {
                        username = u.GetString()?.Trim();
                        if (!string.IsNullOrEmpty(username))
                        {
                            break;
                        }
                    }
                }

                var passProps = new[] { "password", "Password", "basicAuthPassword", "BasicAuthPassword" };
                foreach (var prop in passProps)
                {
                    if (root.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        password = p.GetString();
                        if (password != null)
                        {
                            break;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
            }
        }

        if (string.IsNullOrEmpty(username) && settings.Contains("username=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(settings, @"(?:^|[&?])(?:username|Username|basicAuthUsername)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                username = Uri.UnescapeDataString(match.Groups[1].Value).Trim();
            }
        }

        if (password == null && settings.Contains("password=", StringComparison.OrdinalIgnoreCase))
        {
            var match = Regex.Match(settings, @"(?:^|[&?])(?:password|Password|basicAuthPassword)=([^&]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                password = Uri.UnescapeDataString(match.Groups[1].Value);
            }
        }

        return (username, password);
    }

    public static HttpMethod ResolveHttpMethod(string implementation, string settings)
    {
        if (string.Equals(implementation, "Pushover", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(implementation, "Telegram", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(implementation, "Discord", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(implementation, "Slack", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(implementation, "Gotify", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(implementation, "Apprise", StringComparison.OrdinalIgnoreCase))
        {
            return HttpMethod.Post;
        }

        if (string.IsNullOrWhiteSpace(settings))
        {
            return HttpMethod.Post;
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                var methodProps = new[] { "method", "Method", "httpMethod", "HttpMethod" };
                foreach (var prop in methodProps)
                {
                    if (root.TryGetProperty(prop, out var m) && m.ValueKind == JsonValueKind.String)
                    {
                        var methodStr = m.GetString()?.Trim().ToUpperInvariant();
                        if (methodStr == "GET")
                        {
                            return HttpMethod.Get;
                        }

                        if (methodStr == "PUT")
                        {
                            return HttpMethod.Put;
                        }

                        if (methodStr == "PATCH")
                        {
                            return HttpMethod.Patch;
                        }

                        if (methodStr == "POST")
                        {
                            return HttpMethod.Post;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
            }
        }

        var matchKeys = new[] { "method=", "Method=", "httpMethod=", "HttpMethod=" };
        foreach (var key in matchKeys)
        {
            if (settings.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(settings, $@"{key}([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var val = Uri.UnescapeDataString(match.Groups[1].Value).Trim().ToUpperInvariant();
                    if (val == "GET")
                    {
                        return HttpMethod.Get;
                    }

                    if (val == "PUT")
                    {
                        return HttpMethod.Put;
                    }

                    if (val == "PATCH")
                    {
                        return HttpMethod.Patch;
                    }

                    if (val == "POST")
                    {
                        return HttpMethod.Post;
                    }
                }
            }
        }

        return HttpMethod.Post;
    }

    public static string ExtractPayloadTemplate(string settings)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return null;
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                var templateProps = new[] { "payloadTemplate", "PayloadTemplate", "bodyTemplate", "BodyTemplate", "template", "Template" };
                foreach (var prop in templateProps)
                {
                    if (root.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String)
                    {
                        var val = p.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            return val;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
            }
        }

        var matchKeys = new[] { "payloadTemplate=", "PayloadTemplate=", "bodyTemplate=", "BodyTemplate=", "template=" };
        foreach (var key in matchKeys)
        {
            if (settings.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(settings, $@"{key}([^&]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var val = Uri.UnescapeDataString(match.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        return val;
                    }
                }
            }
        }

        return null;
    }

    public static string GetSlackColor(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "#4A154B";
        }

        var lower = eventType.ToLowerInvariant();
        if (lower.Contains("healthissue") || lower.Contains("error") || lower.Contains("failed") || lower.Contains("crash"))
        {
            return "#E01E5A";
        }

        if (lower.Contains("complete") || lower.Contains("finished") || lower.Contains("success") || lower.Contains("extracted"))
        {
            return "#2EB67D";
        }

        if (lower.Contains("added") || lower.Contains("grab") || lower.Contains("started"))
        {
            return "#36C5F0";
        }

        if (lower.Contains("warn") || lower.Contains("interact") || lower.Contains("seedgoal"))
        {
            return "#ECB22E";
        }

        return "#4A154B";
    }

    public static int GetDiscordColor(string eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return 0x5865F2;
        }

        var lower = eventType.ToLowerInvariant();
        if (lower.Contains("healthissue") || lower.Contains("error") || lower.Contains("failed") || lower.Contains("crash"))
        {
            return 0xED4245;
        }

        if (lower.Contains("complete") || lower.Contains("finished") || lower.Contains("success") || lower.Contains("extracted"))
        {
            return 0x57F287;
        }

        if (lower.Contains("added") || lower.Contains("grab") || lower.Contains("started"))
        {
            return 0x3BA55D;
        }

        if (lower.Contains("warn") || lower.Contains("interact") || lower.Contains("seedgoal"))
        {
            return 0xFEE75C;
        }

        return 0x5865F2;
    }

    public static string EscapeSlackMrkdwn(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    public static int ExtractPriority(string settings, int defaultPriority = 5)
    {
        if (string.IsNullOrWhiteSpace(settings))
        {
            return defaultPriority;
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.TryGetProperty("priority", out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pNum))
                    {
                        return pNum;
                    }

                    if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var pStr))
                    {
                        return pStr;
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
            }
        }
        else
        {
            var match = Regex.Match(trimmed, @"(?:^|[&?])(?:priority|Priority)=(\d+)", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var val))
            {
                return val;
            }
        }

        return defaultPriority;
    }

    public static (int Priority, int Retry, int Expire, string Device, string Sound) ExtractPushoverSettings(string settings)
    {
        var priority = 0;
        var retry = 60;
        var expire = 3600;
        string device = null;
        string sound = null;

        if (string.IsNullOrWhiteSpace(settings))
        {
            return (priority, retry, expire, device, sound);
        }

        var trimmed = settings.Trim();
        if (trimmed.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("priority", out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var pNum))
                    {
                        priority = pNum;
                    }
                    else if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var pStr))
                    {
                        priority = pStr;
                    }
                }

                if (root.TryGetProperty("retry", out var r) || root.TryGetProperty("Retry", out r) || root.TryGetProperty("retrySeconds", out r))
                {
                    if (r.ValueKind == JsonValueKind.Number && r.TryGetInt32(out var rNum))
                    {
                        retry = rNum;
                    }
                    else if (r.ValueKind == JsonValueKind.String && int.TryParse(r.GetString(), out var rStr))
                    {
                        retry = rStr;
                    }
                }

                if (root.TryGetProperty("expire", out var e) || root.TryGetProperty("Expire", out e) || root.TryGetProperty("expireSeconds", out e))
                {
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var eNum))
                    {
                        expire = eNum;
                    }
                    else if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var eStr))
                    {
                        expire = eStr;
                    }
                }

                if (root.TryGetProperty("device", out var d) || root.TryGetProperty("Device", out d))
                {
                    var dVal = d.GetString();
                    if (!string.IsNullOrWhiteSpace(dVal))
                    {
                        device = dVal.Trim();
                    }
                }

                if (root.TryGetProperty("sound", out var s) || root.TryGetProperty("Sound", out s))
                {
                    var sVal = s.GetString();
                    if (!string.IsNullOrWhiteSpace(sVal))
                    {
                        sound = sVal.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
            }
        }
        else
        {
            var pMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:priority|Priority)=(-?\d+)", RegexOptions.IgnoreCase);
            if (pMatch.Success && int.TryParse(pMatch.Groups[1].Value, out var pVal))
            {
                priority = pVal;
            }

            var rMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:retry|Retry|retrySeconds|RetrySeconds)=(\d+)", RegexOptions.IgnoreCase);
            if (rMatch.Success && int.TryParse(rMatch.Groups[1].Value, out var rVal))
            {
                retry = rVal;
            }

            var eMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:expire|Expire|expireSeconds|ExpireSeconds)=(\d+)", RegexOptions.IgnoreCase);
            if (eMatch.Success && int.TryParse(eMatch.Groups[1].Value, out var eVal))
            {
                expire = eVal;
            }

            var dMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:device|Device)=([^&]+)", RegexOptions.IgnoreCase);
            if (dMatch.Success)
            {
                device = Uri.UnescapeDataString(dMatch.Groups[1].Value);
            }

            var sMatch = Regex.Match(trimmed, @"(?:^|[&?])(?:sound|Sound)=([^&]+)", RegexOptions.IgnoreCase);
            if (sMatch.Success)
            {
                sound = Uri.UnescapeDataString(sMatch.Groups[1].Value);
            }
        }

        priority = Math.Clamp(priority, -2, 2);
        return (priority, retry, expire, device, sound);
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
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
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
            catch (JsonException)
            {
                // Fall back to alternative parsing strategy when JSON parsing fails
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
                    catch (JsonException)
                    {
                        // Fall back to alternative parsing strategy when JSON parsing fails
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
            var (discordUsername, avatarUrl) = ExtractDiscordSettings(settings);
            var title = Truncate($"[{eventType}] {torrentName}", 256);
            var torrentDetails = torrent != null
                ? $"Category: {torrent.Category ?? "None"} | Status: {torrent.Status} | Progress: {torrent.Progress * 100:F1}% | Size: {torrent.TotalSize / (1024.0 * 1024.0):F2} MB"
                : ExtractMessage(genericPayload, $"Event: {eventType}");

            var overview = ExtractOverview(meta);
            var rawDesc = !string.IsNullOrWhiteSpace(overview)
                ? $"{torrentDetails}\n\n{overview}"
                : torrentDetails;
            var desc = Truncate(rawDesc, 2048);
            var color = GetDiscordColor(eventType);

            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                return new
                {
                    username = discordUsername,
                    avatar_url = avatarUrl,
                    embeds = new object[]
                    {
                        new
                        {
                            title,
                            description = desc,
                            color,
                            timestamp = DateTime.UtcNow.ToString("o"),
                        },
                    },
                };
            }

            return new
            {
                username = discordUsername,
                embeds = new object[]
                {
                    new
                    {
                        title,
                        description = desc,
                        color,
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

        var template = ExtractPayloadTemplate(settings);
        if (!string.IsNullOrWhiteSpace(template))
        {
            return InterpolateTemplate(template, eventType, torrent, meta, genericPayload);
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
        catch (Exception)
        {
            try
            {
                var prop = ((object)meta).GetType().GetProperty("Overview");
                if (prop != null)
                {
                    return prop.GetValue((object)meta) as string;
                }
            }
            catch (Exception)
            {
                // Ignore missing dynamic Overview property fallback
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

    public static bool IsLikelyJson(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        var trimmed = template.Trim();
        if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
            (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
        {
            if (trimmed.StartsWith("{") && trimmed.EndsWith("}") && !trimmed.Contains(':') && !trimmed.Contains(','))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    public static string EscapeJsonString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return HttpUtility.JavaScriptStringEncode(value);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            return "0 B";
        }

        return bytes switch
        {
            >= 1024L * 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L * 1024L):F2} TB",
            >= 1024L * 1024L * 1024L => $"{(double)bytes / (1024L * 1024L * 1024L):F2} GB",
            >= 1024L * 1024L => $"{(double)bytes / (1024L * 1024L):F2} MB",
            >= 1024L => $"{(double)bytes / 1024L:F2} KB",
            _ => $"{bytes} B"
        };
    }

    public static string FormatSpeed(long bytesPerSec)
    {
        return $"{FormatBytes(bytesPerSec)}/s";
    }

    public static string InterpolateTemplate(
        string template,
        string eventType = null,
        Torrent torrent = null,
        object meta = null,
        object genericPayload = null,
        bool? isJson = null)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        var isJsonTemplate = isJson ?? IsLikelyJson(template);

        return Regex.Replace(template, @"\{([A-Za-z0-9_.]+)\}", match =>
        {
            var token = match.Groups[1].Value;
            var lower = token.ToLowerInvariant();

            string ResolveString(string s) => isJsonTemplate ? EscapeJsonString(s) : (s ?? string.Empty);

            switch (lower)
            {
                case "eventtype":
                    return ResolveString(eventType ?? ExtractPropertyString(genericPayload, "eventType") ?? string.Empty);

                case "instancename":
                    return ResolveString(ExtractPropertyString(genericPayload, "instanceName") ?? "Leecharr");

                case "timestamp":
                    return ResolveString(ExtractPropertyString(genericPayload, "timestamp") ?? DateTime.UtcNow.ToString("o"));

                case "torrent.id":
                    var id = torrent?.Id ?? ExtractPropertyLong(genericPayload, "torrent", "id") ?? 0;
                    return id.ToString(CultureInfo.InvariantCulture);

                case "torrent.name":
                case "torrent.title":
                    var tName = torrent?.Name ?? ExtractPropertyString(genericPayload, "torrent", "name") ?? ExtractPropertyString(genericPayload, "torrent", "title") ?? ExtractMessage(genericPayload, eventType ?? string.Empty) ?? string.Empty;
                    return ResolveString(tName);

                case "torrent.infohash":
                    var hash = torrent?.InfoHash ?? ExtractPropertyString(genericPayload, "torrent", "infoHash") ?? string.Empty;
                    return ResolveString(hash);

                case "torrent.category":
                    var cat = torrent?.Category ?? ExtractPropertyString(genericPayload, "torrent", "category") ?? string.Empty;
                    return ResolveString(cat);

                case "torrent.status":
                    var stat = torrent != null ? torrent.Status.ToString() : (ExtractPropertyString(genericPayload, "torrent", "status") ?? ExtractPropertyString(genericPayload, "torrent", "state") ?? string.Empty);
                    return ResolveString(stat);

                case "torrent.size":
                case "torrent.sizeformatted":
                    var sfBytes = torrent?.TotalSize ?? ExtractPropertyLong(genericPayload, "torrent", "totalSize") ?? ExtractPropertyLong(genericPayload, "torrent", "size") ?? 0;
                    return ResolveString(FormatBytes(sfBytes));

                case "torrent.totalsize":
                    var ts = torrent?.TotalSize ?? ExtractPropertyLong(genericPayload, "torrent", "totalSize") ?? 0;
                    return ts.ToString(CultureInfo.InvariantCulture);

                case "torrent.downloaded":
                    var dl = torrent?.Downloaded ?? ExtractPropertyLong(genericPayload, "torrent", "downloaded") ?? 0;
                    return dl.ToString(CultureInfo.InvariantCulture);

                case "torrent.uploaded":
                    var ul = torrent?.Uploaded ?? ExtractPropertyLong(genericPayload, "torrent", "uploaded") ?? 0;
                    return ul.ToString(CultureInfo.InvariantCulture);

                case "torrent.ratio":
                    var ratio = torrent != null ? (double.IsFinite(torrent.Ratio) ? torrent.Ratio : 0.0) : (ExtractPropertyDouble(genericPayload, "torrent", "ratio") ?? 0.0);
                    return ratio.ToString("0.##", CultureInfo.InvariantCulture);

                case "torrent.progress":
                    var rawProg = torrent != null ? (double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0) : (ExtractPropertyDouble(genericPayload, "torrent", "progress") ?? 0.0);
                    var progRatio = rawProg > 1.0 ? rawProg / 100.0 : rawProg;
                    return progRatio.ToString("0.####", CultureInfo.InvariantCulture);

                case "torrent.progresspercent":
                    var rawProgPct = torrent != null ? (double.IsFinite(torrent.Progress) ? torrent.Progress : 0.0) : (ExtractPropertyDouble(genericPayload, "torrent", "progress") ?? 0.0);
                    var progPct = rawProgPct <= 1.0 ? rawProgPct * 100.0 : rawProgPct;
                    return progPct.ToString("0.##", CultureInfo.InvariantCulture);

                case "torrent.downloadspeedformatted":
                    var dSpeed = torrent?.DownloadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "downloadSpeed") ?? 0;
                    return ResolveString(FormatSpeed(dSpeed));

                case "torrent.uploadspeedformatted":
                    var uSpeed = torrent?.UploadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "uploadSpeed") ?? 0;
                    return ResolveString(FormatSpeed(uSpeed));

                case "torrent.downloadspeed":
                    var rawDSpeed = torrent?.DownloadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "downloadSpeed") ?? 0;
                    return rawDSpeed.ToString(CultureInfo.InvariantCulture);

                case "torrent.uploadspeed":
                    var rawUSpeed = torrent?.UploadSpeed ?? ExtractPropertyLong(genericPayload, "torrent", "uploadSpeed") ?? 0;
                    return rawUSpeed.ToString(CultureInfo.InvariantCulture);

                case "torrent.etastring":
                    var etaStr = ExtractPropertyString(genericPayload, "torrent", "etaString") ?? (torrent != null ? $"{torrent.Eta}s" : "00:00:00");
                    return ResolveString(etaStr);

                case "torrent.eta":
                    var rawEta = torrent != null ? torrent.Eta : (ExtractPropertyLong(genericPayload, "torrent", "eta") ?? 0);
                    return rawEta.ToString(CultureInfo.InvariantCulture);

                case "torrent.savepath":
                    var savePath = torrent?.SavePath ?? ExtractPropertyString(genericPayload, "torrent", "savePath") ?? ExtractPropertyString(genericPayload, "torrent", "downloadPath") ?? string.Empty;
                    return ResolveString(savePath);

                case "media.title":
                    var mTitle = ExtractMediaTitle(meta) ?? ExtractPropertyString(genericPayload, "media", "title") ?? torrent?.Name ?? string.Empty;
                    return ResolveString(mTitle);

                case "media.year":
                    var mYear = ExtractMediaYear(meta) ?? ExtractPropertyInt(genericPayload, "media", "year") ?? 0;
                    return mYear.ToString(CultureInfo.InvariantCulture);

                case "media.overview":
                    var mOverview = ExtractOverview(meta) ?? ExtractPropertyString(genericPayload, "media", "overview") ?? string.Empty;
                    return ResolveString(mOverview);

                case "message":
                    var msg = ExtractMessage(genericPayload, string.Empty);
                    return ResolveString(msg);

                default:
                    return match.Value;
            }
        });
    }

    internal static object GetNestedProperty(object obj, params string[] propertyNames)
    {
        if (obj == null || propertyNames == null || propertyNames.Length == 0)
        {
            return null;
        }

        var current = obj;
        foreach (var propName in propertyNames)
        {
            if (current == null)
            {
                return null;
            }

            current = GetSingleProperty(current, propName);
        }

        return current;
    }

    private static object GetSingleProperty(object obj, string propName)
    {
        if (obj == null || string.IsNullOrWhiteSpace(propName))
        {
            return null;
        }

        if (obj is IDictionary<string, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (string.Equals(kvp.Key, propName, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        if (obj is JsonElement jsonElem)
        {
            if (jsonElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in jsonElem.EnumerateObject())
                {
                    if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value;
                    }
                }
            }

            return null;
        }

        var propInfo = obj.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return propInfo?.GetValue(obj);
    }

    internal static string ExtractPropertyString(object obj, params string[] path)
    {
        var val = GetNestedProperty(obj, path);
        if (val == null)
        {
            return null;
        }

        if (val is JsonElement elem)
        {
            return elem.ValueKind == JsonValueKind.String ? elem.GetString() : elem.GetRawText();
        }

        return val.ToString();
    }

    internal static long? ExtractPropertyLong(object obj, params string[] path)
    {
        var val = GetNestedProperty(obj, path);
        if (val == null)
        {
            return null;
        }

        if (val is long l)
        {
            return l;
        }

        if (val is int i)
        {
            return i;
        }

        if (val is double d)
        {
            return (long)d;
        }

        if (val is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetInt64(out var jsonLong))
            {
                return jsonLong;
            }

            if (elem.ValueKind == JsonValueKind.String && long.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedLong))
            {
                return parsedLong;
            }
        }

        if (long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    internal static double? ExtractPropertyDouble(object obj, params string[] path)
    {
        var val = GetNestedProperty(obj, path);
        if (val == null)
        {
            return null;
        }

        if (val is double d)
        {
            return d;
        }

        if (val is float f)
        {
            return f;
        }

        if (val is int i)
        {
            return i;
        }

        if (val is long l)
        {
            return l;
        }

        if (val is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Number && elem.TryGetDouble(out var jsonDouble))
            {
                return jsonDouble;
            }

            if (elem.ValueKind == JsonValueKind.String && double.TryParse(elem.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return parsedDouble;
            }
        }

        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    internal static int? ExtractPropertyInt(object obj, params string[] path)
    {
        var l = ExtractPropertyLong(obj, path);
        return l.HasValue ? (int)l.Value : null;
    }

    internal static string ExtractMediaTitle(object meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            var prop = meta.GetType().GetProperty("Title", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return prop?.GetValue(meta)?.ToString();
        }
        catch (Exception)
        {
            // Ignore reflection errors on non-compliant metadata models
            return null;
        }
    }

    internal static int? ExtractMediaYear(object meta)
    {
        if (meta == null)
        {
            return null;
        }

        try
        {
            var prop = meta.GetType().GetProperty("Year", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            var val = prop?.GetValue(meta);
            if (val is int i && i > 0)
            {
                return i;
            }

            if (val is string s && int.TryParse(s, out var parsed) && parsed > 0)
            {
                return parsed;
            }
        }
        catch (Exception)
        {
            // Ignore reflection errors on non-compliant metadata models
        }

        return null;
    }

    internal static string ExtractErrorMessage(object payload)
    {
        if (payload == null)
        {
            return null;
        }

        var val = ExtractPropertyString(payload, "error") ??
                  ExtractPropertyString(payload, "Error") ??
                  ExtractPropertyString(payload, "errorMessage") ??
                  ExtractPropertyString(payload, "ErrorMessage") ??
                  ExtractPropertyString(payload, "exception") ??
                  ExtractPropertyString(payload, "Exception") ??
                  ExtractPropertyString(payload, "message") ??
                  ExtractPropertyString(payload, "Message");

        return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
    }
}
