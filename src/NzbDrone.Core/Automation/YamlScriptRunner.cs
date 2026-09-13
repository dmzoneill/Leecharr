#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Torrents;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NzbDrone.Core.Automation;

public class YamlScriptRunner : IScriptRunner
{
    private static readonly Regex VariableRegex = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);
    private readonly IManageCommandQueue? _commandQueue;
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    public YamlScriptRunner(IManageCommandQueue? commandQueue = null)
    {
        _commandQueue = commandQueue;
    }

    public AutomationExecutionResult Execute(
        AutomationScript script,
        Torrent? torrent = null,
        List<string>? torrentTags = null,
        Dictionary<string, object>? customInputs = null)
    {
        var result = new AutomationExecutionResult();
        var logBuilder = new StringBuilder();
        var sw = Stopwatch.StartNew();
        var systemContext = new ScriptSystemContext(_commandQueue);

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            using var reader = new StringReader(script.Code ?? string.Empty);
            var parsedYaml = deserializer.Deserialize<YamlWorkflowModel>(reader);

            if (parsedYaml == null || parsedYaml.Steps == null || parsedYaml.Steps.Count == 0)
            {
                result.Success = true;
                logBuilder.AppendLine("[INFO] Empty YAML workflow executed successfully.");
                return result;
            }

            var variableContext = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            // Populate inputs / secrets
            var mergedInputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(script.InputsJson))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(script.InputsJson);
                    if (parsed != null)
                    {
                        foreach (var kvp in parsed)
                        {
                            mergedInputs[kvp.Key] = JsonElementToObject(kvp.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to parse inputs for YAML script {0}", script.Name);
                }
            }

            if (customInputs != null)
            {
                foreach (var kvp in customInputs)
                {
                    mergedInputs[kvp.Key] = kvp.Value;
                }
            }

            foreach (var kvp in mergedInputs)
            {
                variableContext[$"inputs.{kvp.Key}"] = kvp.Value;
                variableContext[$"secrets.{kvp.Key}"] = kvp.Value;
            }

            // Populate torrent context
            ScriptTorrentContext? torrentCtx = null;
            if (torrent != null)
            {
                torrentCtx = new ScriptTorrentContext(torrent, result, torrentTags);
                variableContext["torrent.id"] = torrentCtx.id;
                variableContext["torrent.name"] = torrentCtx.name;
                variableContext["torrent.infoHash"] = torrentCtx.infoHash;
                variableContext["torrent.size"] = torrentCtx.size;
                variableContext["torrent.ratio"] = torrentCtx.ratio;
                variableContext["torrent.category"] = torrentCtx.category;
                variableContext["torrent.tracker"] = torrentCtx.tracker;
                variableContext["torrent.status"] = torrentCtx.status;
                variableContext["torrent.progress"] = torrentCtx.progress;
            }

            var httpClient = new ScriptHttpContext();

            // Run steps sequentially
            foreach (var step in parsedYaml.Steps)
            {
                var stepName = step.Name ?? "Unnamed Step";
                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [STEP] Starting: {stepName}");

                // Check condition if specified
                if (!string.IsNullOrWhiteSpace(step.Condition))
                {
                    if (!EvaluateSimpleCondition(step.Condition, variableContext))
                    {
                        logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [SKIP] Condition skipped: {step.Condition}");
                        continue;
                    }
                }

                // Execute HTTP step
                if (step.Http != null)
                {
                    var url = SubstituteVariables(step.Http.Url ?? string.Empty, variableContext);
                    var method = (step.Http.Method ?? "GET").ToUpperInvariant();

                    var options = new Dictionary<string, object>();
                    if (step.Http.Headers != null && step.Http.Headers.Count > 0)
                    {
                        var headers = new Dictionary<string, object>();
                        foreach (var kvp in step.Http.Headers)
                        {
                            headers[kvp.Key] = SubstituteVariables(kvp.Value?.ToString() ?? string.Empty, variableContext);
                        }

                        options["headers"] = headers;
                    }

                    if (step.Http.Cookies != null)
                    {
                        if (step.Http.Cookies is string cookieStr)
                        {
                            options["cookies"] = SubstituteVariables(cookieStr, variableContext);
                        }
                        else if (step.Http.Cookies is Dictionary<object, object> cookieDict)
                        {
                            var cMap = new Dictionary<string, object>();
                            foreach (var kvp in cookieDict)
                            {
                                cMap[kvp.Key.ToString()!] = SubstituteVariables(kvp.Value?.ToString() ?? string.Empty, variableContext);
                            }

                            options["cookies"] = cMap;
                        }
                    }

                    if (step.Http.Json == true)
                    {
                        options["json"] = true;
                    }

                    object? body = null;
                    if (step.Http.Body != null)
                    {
                        if (step.Http.Body is string bodyStr)
                        {
                            body = SubstituteVariables(bodyStr, variableContext);
                        }
                        else if (step.Http.Body is Dictionary<object, object> dictBody)
                        {
                            var bMap = new Dictionary<string, object>();
                            foreach (var kvp in dictBody)
                            {
                                var valStr = kvp.Value?.ToString() ?? string.Empty;
                                bMap[kvp.Key.ToString()!] = SubstituteVariables(valStr, variableContext);
                            }

                            body = bMap;
                        }
                    }

                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [HTTP] {method} {url}");

                    object response;
                    switch (method)
                    {
                        case "POST":
                            response = httpClient.post(url, body, options);
                            break;
                        case "PUT":
                            response = httpClient.put(url, body, options);
                            break;
                        case "DELETE":
                            response = httpClient.delete(url, options);
                            break;
                        default:
                            response = httpClient.get(url, options);
                            break;
                    }

                    if (!string.IsNullOrWhiteSpace(step.Register) && response is Dictionary<string, object?> respDict)
                    {
                        var regKey = step.Register.Trim();
                        foreach (var kvp in respDict)
                        {
                            variableContext[$"{regKey}.{kvp.Key}"] = kvp.Value;
                        }

                        variableContext[regKey] = respDict;
                        logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [REGISTER] Saved response to '${{{regKey}}}' (status: {respDict.GetValueOrDefault("status")})");
                    }
                }

                // Execute actions
                if (step.Actions != null)
                {
                    foreach (var action in step.Actions)
                    {
                        if (action.TryGetValue("command", out var cmdName) && cmdName != null)
                        {
                            var cmd = SubstituteVariables(cmdName.ToString()!, variableContext);
                            systemContext.runCommand(cmd);
                            logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] runCommand: {cmd}");
                        }

                        if (torrentCtx != null)
                        {
                            if (action.TryGetValue("addTag", out var tagToAdd) && tagToAdd != null)
                            {
                                var tag = SubstituteVariables(tagToAdd.ToString()!, variableContext);
                                torrentCtx.addTag(tag);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] addTag: {tag}");
                            }

                            if (action.TryGetValue("removeTag", out var tagToRemove) && tagToRemove != null)
                            {
                                var tag = SubstituteVariables(tagToRemove.ToString()!, variableContext);
                                torrentCtx.removeTag(tag);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] removeTag: {tag}");
                            }

                            if (action.TryGetValue("setCategory", out var newCat) && newCat != null)
                            {
                                var cat = SubstituteVariables(newCat.ToString()!, variableContext);
                                torrentCtx.setCategory(cat);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setCategory: {cat}");
                            }

                            if (action.ContainsKey("pause"))
                            {
                                torrentCtx.pause();
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] pause");
                            }

                            if (action.ContainsKey("resume"))
                            {
                                torrentCtx.resume();
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] resume");
                            }

                            if (action.ContainsKey("recheck"))
                            {
                                torrentCtx.recheck();
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] recheck");
                            }

                            if (action.ContainsKey("reannounce"))
                            {
                                torrentCtx.reannounce();
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] reannounce");
                            }

                            if (action.ContainsKey("remove"))
                            {
                                var deleteData = action.TryGetValue("deleteData", out var dd) && (dd is true || dd?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);
                                torrentCtx.remove(deleteData);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] remove (deleteData: {deleteData})");
                            }
                        }
                    }
                }
            }

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"YAML Execution error: {ex.Message}";
            logBuilder.AppendLine($"[ERROR] {ex.Message}");
        }
        finally
        {
            sw.Stop();
            result.ExecutionTimeMs = sw.ElapsedMilliseconds;
            result.OutputLog = logBuilder.ToString();
        }

        return result;
    }

    private static string SubstituteVariables(string template, Dictionary<string, object?> context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return VariableRegex.Replace(template, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (context.TryGetValue(key, out var val) && val != null)
            {
                return val.ToString() ?? string.Empty;
            }

            return match.Value;
        });
    }

    private static bool EvaluateSimpleCondition(string condition, Dictionary<string, object?> context)
    {
        var substituted = SubstituteVariables(condition, context);
        if (string.IsNullOrWhiteSpace(substituted))
        {
            return true;
        }

        if (substituted.Contains("=="))
        {
            var parts = substituted.Split(new[] { "==" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return string.Equals(parts[0].Trim('\'', '"'), parts[1].Trim('\'', '"'), StringComparison.OrdinalIgnoreCase);
            }
        }
        else if (substituted.Contains("!="))
        {
            var parts = substituted.Split(new[] { "!=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return !string.Equals(parts[0].Trim('\'', '"'), parts[1].Trim('\'', '"'), StringComparison.OrdinalIgnoreCase);
            }
        }
        else if (substituted.Contains(">="))
        {
            var parts = substituted.Split(new[] { ">=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var r))
            {
                return l >= r;
            }
        }
        else if (substituted.Contains("<="))
        {
            var parts = substituted.Split(new[] { "<=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var r))
            {
                return l <= r;
            }
        }
        else if (substituted.Contains('>'))
        {
            var parts = substituted.Split(new[] { '>' }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var r))
            {
                return l > r;
            }
        }
        else if (substituted.Contains('<'))
        {
            var parts = substituted.Split(new[] { '<' }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], out var l) && double.TryParse(parts[1], out var r))
            {
                return l < r;
            }
        }

        return !string.Equals(substituted, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => element.ToString(),
            JsonValueKind.Object => element.ToString(),
            _ => element.ToString(),
        };
    }
}

public class YamlWorkflowModel
{
    public string? Name { get; set; }

    public string? Trigger { get; set; }

    public List<YamlStepModel>? Steps { get; set; }
}

public class YamlStepModel
{
    public string? Name { get; set; }

    public string? Condition { get; set; }

    public YamlHttpStepModel? Http { get; set; }

    public string? Register { get; set; }

    public List<Dictionary<string, object>>? Actions { get; set; }
}

public class YamlHttpStepModel
{
    public string? Method { get; set; }

    public string? Url { get; set; }

    public Dictionary<string, object>? Headers { get; set; }

    public object? Cookies { get; set; }

    public bool? Json { get; set; }

    public object? Body { get; set; }
}
