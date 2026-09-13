#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        var systemContext = new ScriptSystemContext(_commandQueue, result);

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
                variableContext["torrent.isPrivate"] = torrent.IsPrivate;
                variableContext["torrent.isComplete"] = torrent.Progress >= 100 || torrent.Status == TorrentStatus.Seeding;
                variableContext["torrent.downloadSpeed"] = torrent.DownloadSpeed;
                variableContext["torrent.uploadSpeed"] = torrent.UploadSpeed;
                variableContext["torrent.seeders"] = torrent.Seeders;
                variableContext["torrent.leechers"] = torrent.Leechers;
                variableContext["torrent.savePath"] = torrent.SavePath ?? string.Empty;
                variableContext["torrent.uploaded"] = torrent.Uploaded;
                variableContext["torrent.downloaded"] = torrent.Downloaded;
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

                        if (action.TryGetValue("sendNotification", out var notifObj) && notifObj != null)
                        {
                            if (notifObj is Dictionary<object, object> nDict)
                            {
                                var nTitle = SubstituteVariables(nDict.TryGetValue("title", out var tVal) ? tVal?.ToString() ?? string.Empty : "Automation Alert", variableContext);
                                var nMsg = SubstituteVariables(nDict.TryGetValue("message", out var mVal) ? mVal?.ToString() ?? string.Empty : string.Empty, variableContext);
                                var nProv = nDict.TryGetValue("provider", out var pVal) ? SubstituteVariables(pVal?.ToString() ?? string.Empty, variableContext) : null;
                                systemContext.sendNotification(nTitle, nMsg, nProv);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] sendNotification: {nTitle} (provider: {nProv ?? "all"})");
                            }
                            else
                            {
                                var nMsg = SubstituteVariables(notifObj.ToString()!, variableContext);
                                systemContext.sendNotification("Automation Alert", nMsg);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] sendNotification: {nMsg}");
                            }
                        }

                        if (action.TryGetValue("notifyArr", out var arrObj) || action.TryGetValue("syncArr", out arrObj))
                        {
                            if (arrObj is Dictionary<object, object> aDict)
                            {
                                var appType = aDict.TryGetValue("appType", out var atVal) ? SubstituteVariables(atVal?.ToString() ?? string.Empty, variableContext) : null;
                                int? instId = null;
                                if (aDict.TryGetValue("instanceId", out var idVal) && int.TryParse(idVal?.ToString(), out var parsedId))
                                {
                                    instId = parsedId;
                                }

                                systemContext.notifyArr(appType, instId);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] notifyArr: appType={appType ?? "all"}, instanceId={instId?.ToString() ?? "all"}");
                            }
                            else if (arrObj is string arrStr && !string.IsNullOrWhiteSpace(arrStr) && !arrStr.Equals("true", StringComparison.OrdinalIgnoreCase))
                            {
                                var appType = SubstituteVariables(arrStr, variableContext);
                                systemContext.notifyArr(appType);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] notifyArr: {appType}");
                            }
                            else
                            {
                                systemContext.notifyArr();
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] notifyArr: all");
                            }
                        }

                        if (action.TryGetValue("runScript", out var scriptObj) && scriptObj != null)
                        {
                            if (scriptObj is Dictionary<object, object> sDict)
                            {
                                var path = SubstituteVariables(sDict.TryGetValue("path", out var pVal) ? pVal?.ToString() ?? string.Empty : string.Empty, variableContext);
                                var timeout = sDict.TryGetValue("timeout", out var toVal) && int.TryParse(toVal?.ToString(), out var toParsed) ? toParsed : 60;
                                var argsObj = sDict.TryGetValue("args", out var aVal) ? aVal : null;
                                systemContext.runScript(path, argsObj, timeout);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] runScript: {path} (timeout={timeout}s)");
                            }
                            else
                            {
                                var path = SubstituteVariables(scriptObj.ToString()!, variableContext);
                                systemContext.runScript(path);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] runScript: {path}");
                            }
                        }

                        if (action.TryGetValue("delay", out var delayVal) || action.TryGetValue("sleep", out delayVal))
                        {
                            var dStr = SubstituteVariables(delayVal?.ToString() ?? "1", variableContext);
                            if (int.TryParse(dStr, out var dSec))
                            {
                                systemContext.delay(dSec);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] delay: {dSec}s");
                            }
                        }

                        if (action.TryGetValue("log", out var logVal) && logVal != null)
                        {
                            if (logVal is Dictionary<object, object> lDict)
                            {
                                var msg = SubstituteVariables(lDict.TryGetValue("message", out var mVal) ? mVal?.ToString() ?? string.Empty : string.Empty, variableContext);
                                var lvl = lDict.TryGetValue("level", out var lvlVal) ? lvlVal?.ToString() ?? "info" : "info";
                                systemContext.log(msg, lvl);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] log ({lvl}): {msg}");
                            }
                            else
                            {
                                var msg = SubstituteVariables(logVal.ToString()!, variableContext);
                                systemContext.log(msg);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] log: {msg}");
                            }
                        }

                        if (action.TryGetValue("setVariable", out var setVarObj) && setVarObj is Dictionary<object, object> svDict)
                        {
                            var vKey = svDict.TryGetValue("key", out var kVal) ? kVal?.ToString() : null;
                            var vVal = svDict.TryGetValue("value", out var valVal) ? SubstituteVariables(valVal?.ToString() ?? string.Empty, variableContext) : string.Empty;
                            if (!string.IsNullOrWhiteSpace(vKey))
                            {
                                variableContext[vKey] = vVal;
                                variableContext[$"variables.{vKey}"] = vVal;
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setVariable: {vKey} = {vVal}");
                            }
                        }

                        if (action.TryGetValue("stopPipeline", out var stopVal))
                        {
                            var reason = stopVal != null ? SubstituteVariables(stopVal.ToString()!, variableContext) : "Condition matched stop";
                            systemContext.stopPipeline(reason);
                            logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] stopPipeline: {reason}");
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

                            if (action.TryGetValue("setUploadLimit", out var upLimitVal) && upLimitVal != null)
                            {
                                var upStr = SubstituteVariables(upLimitVal.ToString()!, variableContext);
                                if (int.TryParse(upStr, out var upLimit))
                                {
                                    torrentCtx.setUploadLimit(upLimit);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setUploadLimit: {upLimit} KB/s");
                                }
                            }

                            if (action.TryGetValue("setDownloadLimit", out var dlLimitVal) && dlLimitVal != null)
                            {
                                var dlStr = SubstituteVariables(dlLimitVal.ToString()!, variableContext);
                                if (int.TryParse(dlStr, out var dlLimit))
                                {
                                    torrentCtx.setDownloadLimit(dlLimit);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setDownloadLimit: {dlLimit} KB/s");
                                }
                            }

                            if (action.TryGetValue("setRatioLimit", out var ratioVal) && ratioVal != null)
                            {
                                var rStr = SubstituteVariables(ratioVal.ToString()!, variableContext);
                                if (double.TryParse(rStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rLimit))
                                {
                                    torrentCtx.setRatioLimit(rLimit);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setRatioLimit: {rLimit:F2}");
                                }
                            }

                            if (action.TryGetValue("setSeedingTimeLimit", out var seedTimeVal) && seedTimeVal != null)
                            {
                                var stStr = SubstituteVariables(seedTimeVal.ToString()!, variableContext);
                                if (int.TryParse(stStr, out var stMinutes))
                                {
                                    torrentCtx.setSeedingTimeLimit(stMinutes);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setSeedingTimeLimit: {stMinutes} min");
                                }
                            }

                            if (action.TryGetValue("setPriority", out var prioVal) && prioVal != null)
                            {
                                var prioStr = SubstituteVariables(prioVal.ToString()!, variableContext);
                                torrentCtx.setPriority(prioStr);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setPriority: {prioStr}");
                            }

                            if (action.TryGetValue("setSequentialDownload", out var seqVal) || action.TryGetValue("setSequential", out seqVal))
                            {
                                var isSeq = seqVal is true || seqVal?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                                torrentCtx.setSequential(isSeq);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setSequentialDownload: {isSeq}");
                            }

                            if (action.TryGetValue("setSuperSeeding", out var ssVal))
                            {
                                var isSs = ssVal is true || ssVal?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
                                torrentCtx.setSuperSeeding(isSs);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] setSuperSeeding: {isSs}");
                            }

                            if (action.TryGetValue("moveFiles", out var moveDest) || action.TryGetValue("setSavePath", out moveDest))
                            {
                                if (moveDest != null)
                                {
                                    var dest = SubstituteVariables(moveDest.ToString()!, variableContext);
                                    torrentCtx.moveFiles(dest);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] moveFiles: {dest}");
                                }
                            }

                            if (action.TryGetValue("addTracker", out var trkToAdd) && trkToAdd != null)
                            {
                                var trk = SubstituteVariables(trkToAdd.ToString()!, variableContext);
                                torrentCtx.addTracker(trk);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] addTracker: {trk}");
                            }

                            if (action.TryGetValue("removeTracker", out var trkToRemove) && trkToRemove != null)
                            {
                                var trk = SubstituteVariables(trkToRemove.ToString()!, variableContext);
                                torrentCtx.removeTracker(trk);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] removeTracker: {trk}");
                            }

                            if (action.ContainsKey("boostTracker"))
                            {
                                torrentCtx.boostTracker();
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] boostTracker");
                            }

                            if (action.TryGetValue("banPeer", out var peerIp) && peerIp != null)
                            {
                                var ip = SubstituteVariables(peerIp.ToString()!, variableContext);
                                torrentCtx.banPeer(ip);
                                logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] banPeer: {ip}");
                            }

                            if (action.TryGetValue("extractArchive", out var extractVal))
                            {
                                if (extractVal is Dictionary<object, object> extDict)
                                {
                                    var dest = extDict.TryGetValue("destination", out var dVal) ? SubstituteVariables(dVal?.ToString() ?? string.Empty, variableContext) : null;
                                    var del = extDict.TryGetValue("deleteArchive", out var daVal) && (daVal is true || daVal?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);
                                    torrentCtx.extractArchive(dest, del);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] extractArchive (dest: {dest ?? "default"}, delete: {del})");
                                }
                                else
                                {
                                    torrentCtx.extractArchive();
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] extractArchive");
                                }
                            }

                            if (action.TryGetValue("cleanUnwantedFiles", out var cleanVal) && cleanVal != null)
                            {
                                if (cleanVal is IEnumerable<object> cleanList)
                                {
                                    var patterns = new List<string>();
                                    foreach (var c in cleanList)
                                    {
                                        if (c != null)
                                        {
                                            patterns.Add(SubstituteVariables(c.ToString()!, variableContext));
                                        }
                                    }

                                    torrentCtx.cleanUnwantedFiles(patterns.ToArray());
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] cleanUnwantedFiles: {string.Join(", ", patterns)}");
                                }
                                else
                                {
                                    var cleanStr = SubstituteVariables(cleanVal.ToString()!, variableContext);
                                    torrentCtx.cleanFiles(cleanStr);
                                    logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [ACTION] cleanUnwantedFiles: {cleanStr}");
                                }
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

                        if (result.ShouldStopPipeline)
                        {
                            break;
                        }
                    }

                    if (result.ShouldStopPipeline)
                    {
                        logBuilder.AppendLine($"[{DateTime.UtcNow:HH:mm:ss}] [STOP] Pipeline halted: {result.StopReason ?? "Stop requested"}");
                        break;
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

        var trimmed = substituted.Trim();

        // Single boolean expression like: !${torrent.isPrivate} or ${torrent.isPrivate}
        if (!trimmed.Contains("==") && !trimmed.Contains("!=") && !trimmed.Contains(">=") && !trimmed.Contains("<=") && !trimmed.Contains('>') && !trimmed.Contains('<'))
        {
            if (trimmed.StartsWith('!'))
            {
                var inner = trimmed[1..].Trim();
                return IsFalsy(inner);
            }

            return !IsFalsy(trimmed);
        }

        if (substituted.Contains("=="))
        {
            var parts = substituted.Split(new[] { "==" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return AreEqual(parts[0], parts[1]);
            }
        }
        else if (substituted.Contains("!="))
        {
            var parts = substituted.Split(new[] { "!=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                return !AreEqual(parts[0], parts[1]);
            }
        }
        else if (substituted.Contains(">="))
        {
            var parts = substituted.Split(new[] { ">=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l >= r;
            }
        }
        else if (substituted.Contains("<="))
        {
            var parts = substituted.Split(new[] { "<=" }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l <= r;
            }
        }
        else if (substituted.Contains('>'))
        {
            var parts = substituted.Split(new[] { '>' }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l > r;
            }
        }
        else if (substituted.Contains('<'))
        {
            var parts = substituted.Split(new[] { '<' }, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && TryParseNumber(parts[0], out var l) && TryParseNumber(parts[1], out var r))
            {
                return l < r;
            }
        }

        return !IsFalsy(substituted);
    }

    private static bool TryParseNumber(string raw, out double number)
    {
        var clean = raw.Trim('\'', '"', ' ');
        return double.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    private static bool IsFalsy(string val)
    {
        var clean = val.Trim('\'', '"', ' ');
        return string.IsNullOrEmpty(clean)
            || string.Equals(clean, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(clean, "null", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreEqual(string leftRaw, string rightRaw)
    {
        var left = leftRaw.Trim('\'', '"', ' ');
        var right = rightRaw.Trim('\'', '"', ' ');

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Check boolean equivalence (e.g. 1 == true, on == true, etc.)
        if (TryParseBool(left, out var bLeft) && TryParseBool(right, out var bRight))
        {
            return bLeft == bRight;
        }

        // Check numeric equivalence (e.g. 5.0 == 5)
        if (TryParseNumber(left, out var nLeft) && TryParseNumber(right, out var nRight))
        {
            return Math.Abs(nLeft - nRight) < 0.000001;
        }

        return false;
    }

    private static bool TryParseBool(string val, out bool result)
    {
        if (bool.TryParse(val, out result))
        {
            return true;
        }

        if (string.Equals(val, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }

        if (string.Equals(val, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(val, "off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }

        result = false;
        return false;
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
