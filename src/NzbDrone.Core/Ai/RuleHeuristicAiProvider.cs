// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.BitTorrent;
using NzbDrone.Core.Torrents;
using NzbDrone.Core.Trackers;

namespace NzbDrone.Core.Ai;

public class RuleHeuristicAiProvider : IAiEngineProvider
{
    private static readonly HashSet<string> DangerousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".scr", ".bat", ".cmd", ".vbs", ".js", ".pif", ".ps1", ".msi", ".com", ".hta", ".lnk", ".dll", ".wsf", ".jar", ".cpl", ".iso",
    };

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".m2ts",
        ".flac", ".mp3", ".aac", ".wav", ".alac", ".ogg", ".m4a",
    };

    private static readonly Regex SeasonEpisodeRegex = new(@"(?i)\bS(?<season>\d{1,2})E(?<episode>\d{1,3})(?:-?E?(?<endEpisode>\d{1,3}))?\b", RegexOptions.Compiled);
    private static readonly Regex AltSeasonEpisodeRegex = new(@"(?i)\b(?<season>\d{1,2})x(?<episode>\d{1,3})\b", RegexOptions.Compiled);
    private static readonly Regex SeasonOnlyRegex = new(@"(?i)\b(?:Season|Series)\s*(?<season>\d{1,2})\b", RegexOptions.Compiled);
    private static readonly Regex EpisodeOnlyRegex = new(@"(?i)\b(?:Episode|Ep)\s*(?<episode>\d{1,3})\b", RegexOptions.Compiled);
    private static readonly Regex YearRegex = new(@"\b(?<year>19\d{2}|20\d{2})\b", RegexOptions.Compiled);
    private static readonly Regex ResolutionRegex = new(@"(?i)\b(?<res>2160p|4k|1080p|1080i|720p|576p|480p|576i|480i)\b", RegexOptions.Compiled);
    private static readonly Regex QualityRegex = new(@"(?i)\b(?<quality>UHD\s*BluRay|BluRay|BRRip|BDRip|WEB-?DL|WEBRip|HDTV|DVDRip|DVD-?R|REMUX|CAM|TeleSync|TS)\b", RegexOptions.Compiled);
    private static readonly Regex VideoCodecRegex = new(@"(?i)\b(?<codec>x265|HEVC|H\.?265|x264|H\.?264|AVC|AV1|XviD|DivX|VC-?1)\b", RegexOptions.Compiled);
    private static readonly Regex AudioCodecRegex = new(@"(?i)\b(?<audio>DTS-HD(?:[\s\.]*MA)?|DTS-X|TrueHD(?:[\s\.]*Atmos)?|Atmos|DTS|E-?AC-?3|DDP5\.1|DDP|AC-?3|DD5\.1|AAC(?:[\s\.]*2\.0)?|FLAC|MP3|OPUS|VORBIS)\b", RegexOptions.Compiled);
    private static readonly Regex AudioChannelsRegex = new(@"(?i)\b(?<channels>7\.1|5\.1|2\.0|1\.0)\b", RegexOptions.Compiled);
    private static readonly Regex DynamicRangeRegex = new(@"(?i)\b(?<hdr>DV|Dolby\s*Vision|HDR10\+|HDR10|HDR|SDR)\b", RegexOptions.Compiled);
    private static readonly Regex EditionRegex = new(@"(?i)\b(?<edition>Extended(?:\s*Cut)?|Director'?s\s*Cut|Unrated|IMAX(?:\s*Enhanced)?|Theatrical|Remastered|Criterion)\b", RegexOptions.Compiled);
    private static readonly Regex LanguageRegex = new(@"(?i)\b(?<lang>MULTi|DUAL|ENG|English|FRENCH|GERMAN|SPANISH|ITA|JAP|RUS)\b", RegexOptions.Compiled);
    private static readonly Regex ReleaseGroupRegex = new(@"(?:-(?<group>[A-Za-z0-9]+)|\[(?<group>[A-Za-z0-9]+)\])$", RegexOptions.Compiled);

    public string ProviderId => "RuleHeuristic";

    public string DisplayName => "Rule-Based Deterministic NLP & Heuristic AI Engine";

    public string Version => "1.0";

    public string Description => "Deterministic rule-based NLP tokenizer, scene release parser, swarm health diagnostician, and malware anomaly classifier (100% offline).";

    public bool IsAvailable => true;

    public AiCapabilities Capabilities =>
        AiCapabilities.SupportsNaturalLanguageSearch |
        AiCapabilities.SupportsReleaseNameParsing |
        AiCapabilities.SupportsDiagnosticCopilot |
        AiCapabilities.SupportsMalwareAnomalyDetection |
        AiCapabilities.SupportsSwarmOptimization |
        AiCapabilities.SupportsLocalOfflineInference;

    public Task<AiHealthResult> ProbeHealthAsync()
    {
        return Task.FromResult(new AiHealthResult
        {
            IsHealthy = true,
            StatusMessage = "Rule & Heuristic AI Engine is operational (100% offline, zero-dependency).",
            LatencyMs = 0,
            ModelName = "Deterministic-Rule-Engine",
            Version = this.Version,
        });
    }

    public Task<AiParsedRelease> ParseReleaseAsync(string releaseName)
    {
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            return Task.FromResult(new AiParsedRelease
            {
                RawTitle = releaseName ?? string.Empty,
                CleanTitle = string.Empty,
                ConfidenceScore = 0.0,
            });
        }

        var result = new AiParsedRelease
        {
            RawTitle = releaseName,
        };

        var working = releaseName.Trim();

        // 1. Check Proper / Repack / Remux
        if (Regex.IsMatch(working, @"(?i)\bPROPER\b"))
        {
            result.IsProper = true;
        }

        if (Regex.IsMatch(working, @"(?i)\b(REPACK|RERIP)\b"))
        {
            result.IsRepack = true;
        }

        if (Regex.IsMatch(working, @"(?i)\bREMUX\b"))
        {
            result.IsRemux = true;
        }

        // 2. Season & Episode
        var seMatch = SeasonEpisodeRegex.Match(working);
        if (seMatch.Success)
        {
            result.Season = int.Parse(seMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
            var ep = int.Parse(seMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
            result.Episode = ep;
            result.Episodes.Add(ep);

            if (seMatch.Groups["endEpisode"].Success && !string.IsNullOrEmpty(seMatch.Groups["endEpisode"].Value))
            {
                var endEp = int.Parse(seMatch.Groups["endEpisode"].Value, CultureInfo.InvariantCulture);
                for (var i = ep + 1; i <= endEp; i++)
                {
                    result.Episodes.Add(i);
                }
            }
        }
        else
        {
            var altSeMatch = AltSeasonEpisodeRegex.Match(working);
            if (altSeMatch.Success)
            {
                result.Season = int.Parse(altSeMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
                var ep = int.Parse(altSeMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                result.Episode = ep;
                result.Episodes.Add(ep);
            }
            else
            {
                var seasonMatch = SeasonOnlyRegex.Match(working);
                if (seasonMatch.Success)
                {
                    result.Season = int.Parse(seasonMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
                }

                var epMatch = EpisodeOnlyRegex.Match(working);
                if (epMatch.Success)
                {
                    var ep = int.Parse(epMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                    result.Episode = ep;
                    result.Episodes.Add(ep);
                }
            }
        }

        // 3. Year
        var yearMatch = YearRegex.Match(working);
        if (yearMatch.Success)
        {
            result.Year = int.Parse(yearMatch.Groups["year"].Value, CultureInfo.InvariantCulture);
        }

        // 4. Resolution
        var resMatch = ResolutionRegex.Match(working);
        if (resMatch.Success)
        {
            var res = resMatch.Groups["res"].Value;
            result.Resolution = res.Equals("4k", StringComparison.OrdinalIgnoreCase) ? "2160p" : res.ToLowerInvariant();
        }

        // 5. Quality
        var qualityMatch = QualityRegex.Match(working);
        if (qualityMatch.Success)
        {
            result.Quality = NormalizeQuality(qualityMatch.Groups["quality"].Value);
        }

        // 6. Video Codec
        var codecMatch = VideoCodecRegex.Match(working);
        if (codecMatch.Success)
        {
            result.VideoCodec = NormalizeCodec(codecMatch.Groups["codec"].Value);
        }

        // 7. Audio Codec & Channels
        var audioMatch = AudioCodecRegex.Match(working);
        if (audioMatch.Success)
        {
            var rawAudio = audioMatch.Groups["audio"].Value.Replace('.', ' ');
            rawAudio = Regex.Replace(rawAudio, @"\s+", " ").Trim();
            result.AudioCodec = NormalizeAudioCodec(rawAudio);
        }

        var channelsMatch = AudioChannelsRegex.Match(working);
        if (channelsMatch.Success)
        {
            result.AudioChannels = channelsMatch.Groups["channels"].Value;
        }

        // 8. Dynamic Range
        var hdrMatch = DynamicRangeRegex.Match(working);
        if (hdrMatch.Success)
        {
            result.DynamicRange = NormalizeHdr(hdrMatch.Groups["hdr"].Value);
        }

        // 9. Edition
        var editionMatch = EditionRegex.Match(working);
        if (editionMatch.Success)
        {
            result.Edition = editionMatch.Groups["edition"].Value;
        }

        // 10. Language
        var langMatch = LanguageRegex.Match(working);
        if (langMatch.Success)
        {
            result.Language = langMatch.Groups["lang"].Value.ToUpperInvariant();
        }

        // 11. Release Group
        var groupMatch = ReleaseGroupRegex.Match(working);
        if (groupMatch.Success)
        {
            result.ReleaseGroup = groupMatch.Groups["group"].Value;
        }

        // 12. Clean Title Extraction
        result.CleanTitle = ExtractCleanTitle(working, result);

        // Confidence calculation
        var featuresCount = 0;
        if (result.Year.HasValue)
        {
            featuresCount++;
        }

        if (result.Season.HasValue)
        {
            featuresCount++;
        }

        if (!string.IsNullOrEmpty(result.Resolution))
        {
            featuresCount++;
        }

        if (!string.IsNullOrEmpty(result.Quality))
        {
            featuresCount++;
        }

        if (!string.IsNullOrEmpty(result.VideoCodec))
        {
            featuresCount++;
        }

        if (!string.IsNullOrEmpty(result.ReleaseGroup))
        {
            featuresCount++;
        }

        result.ConfidenceScore = Math.Min(1.0, 0.4 + (featuresCount * 0.12));

        return Task.FromResult(result);
    }

    public Task<AiDiagnosticReport> DiagnoseTorrentHealthAsync(Torrent torrent, IReadOnlyList<PeerInfo> peers, IReadOnlyList<TrackerEntry> trackers)
    {
        if (torrent == null)
        {
            return Task.FromResult(new AiDiagnosticReport
            {
                OverallHealth = "Unknown",
                Severity = "High",
                Summary = "No torrent data provided for diagnostics.",
                HealthScore = 0.0,
            });
        }

        var issues = new List<string>();
        var recommendations = new List<string>();
        var suggestedActions = new List<string>();
        var healthScore = 100.0;

        peers ??= Array.Empty<PeerInfo>();
        trackers ??= Array.Empty<TrackerEntry>();

        var totalSeeds = Math.Max(torrent.Seeders, peers.Count(p => p.Progress >= 0.999));
        var totalLeechers = Math.Max(torrent.Leechers, peers.Count(p => p.Progress < 0.999));
        var connectedPeersCount = peers.Count;
        var activeDownloadSpeed = torrent.DownloadSpeed > 0 ? torrent.DownloadSpeed : peers.Sum(p => p.DownloadSpeed);
        var activeUploadSpeed = torrent.UploadSpeed > 0 ? torrent.UploadSpeed : peers.Sum(p => p.UploadSpeed);

        // 1. Swarm Health & Seeders Analysis
        if (torrent.Progress < 1.0 && torrent.Status == TorrentStatus.Downloading)
        {
            if (totalSeeds == 0 && connectedPeersCount == 0)
            {
                healthScore -= 60;
                issues.Add("Swarm is dead or stalled: No connected peers or active seeders found.");
                recommendations.Add("Ensure DHT and PEX are enabled to discover unannounced peers.");
                suggestedActions.Add("Add alternative public/DHT trackers or wait for original seeder to reconnect.");
            }
            else if (totalSeeds == 0)
            {
                healthScore -= 35;
                issues.Add("No complete seeders detected in swarm. Only partial peers (leechers) are present.");
                recommendations.Add("Swarm may not contain 100% of all pieces. Monitor piece availability.");
                suggestedActions.Add("Force reannounce to discover newly connected seeders.");
            }
            else if (activeDownloadSpeed == 0 && connectedPeersCount > 0)
            {
                healthScore -= 20;
                issues.Add($"Connected to {connectedPeersCount} peers but download speed is 0 KB/s (all peers may have choked you or lack wanted pieces).");
                recommendations.Add("Check network port forwarding and peer queue limits.");
                suggestedActions.Add("Switch to sequential download or prioritize high-availability pieces.");
            }
        }

        // 2. Seeding & Ratio Analysis
        if (torrent.Progress >= 1.0 || torrent.Status == TorrentStatus.Seeding)
        {
            if (torrent.TargetRatio > 0 && torrent.Ratio >= torrent.TargetRatio)
            {
                recommendations.Add($"Target seed ratio ({torrent.TargetRatio:F2}) achieved (Current: {torrent.Ratio:F2}).");
                suggestedActions.Add("Pause or archive torrent to free upload slots.");
            }
            else if (totalLeechers == 0 && activeUploadSpeed == 0)
            {
                recommendations.Add("No active leechers currently in swarm. Torrent is idle.");
            }
        }

        // 3. Tracker Health Analysis
        var trackerIssuesCount = 0;
        foreach (var tracker in trackers)
        {
            if (!string.IsNullOrEmpty(tracker.ErrorMessage))
            {
                trackerIssuesCount++;
            }
            else if (tracker.ConsecutiveFailures >= 3)
            {
                trackerIssuesCount++;
            }
        }

        if (trackers.Count > 0 && trackerIssuesCount == trackers.Count)
        {
            healthScore -= 30;
            issues.Add("All configured trackers failed to announce or are unreachable.");
            recommendations.Add("Verify Internet connection, DNS resolution, and proxy/VPN binding.");
            suggestedActions.Add("Force reannounce on all trackers or update tracker URLs.");
        }
        else if (trackerIssuesCount > 0)
        {
            healthScore -= 10;
            issues.Add($"{trackerIssuesCount} of {trackers.Count} tracker(s) reported errors or failed announces.");
        }

        // 4. Determine overall health status & severity
        healthScore = Math.Clamp(healthScore, 0.0, 100.0);

        string overallHealth;
        string severity;

        if (torrent.Status == TorrentStatus.Paused || torrent.Status == TorrentStatus.Stopped)
        {
            overallHealth = "Paused";
            severity = "None";
        }
        else if (torrent.Progress >= 1.0)
        {
            overallHealth = "Completed";
            severity = "None";
        }
        else if (healthScore >= 80)
        {
            overallHealth = "Healthy";
            severity = "None";
        }
        else if (healthScore >= 50)
        {
            overallHealth = "Warning";
            severity = "Medium";
        }
        else if (healthScore >= 25)
        {
            overallHealth = "Stalled";
            severity = "High";
        }
        else
        {
            overallHealth = "Dead";
            severity = "Critical";
        }

        var swarmSummary = $"Peers: {connectedPeersCount} connected | Seeders: {totalSeeds} | Leechers: {totalLeechers} | DL: {activeDownloadSpeed / 1024} KB/s | UL: {activeUploadSpeed / 1024} KB/s";
        var trackerSummary = trackers.Count > 0
            ? $"Trackers: {trackers.Count} total ({trackers.Count - trackerIssuesCount} working, {trackerIssuesCount} failing)"
            : "Trackers: None configured";

        var summary = issues.Count == 0
            ? $"Torrent is performing nominally ({overallHealth}). {swarmSummary}."
            : $"Identified {issues.Count} issue(s) affecting torrent performance. {swarmSummary}.";

        return Task.FromResult(new AiDiagnosticReport
        {
            TorrentId = torrent.Id,
            TorrentName = torrent.Name ?? "Unknown",
            OverallHealth = overallHealth,
            Severity = severity,
            Summary = summary,
            Issues = issues,
            Recommendations = recommendations,
            SuggestedActions = suggestedActions,
            SwarmAnalysis = swarmSummary,
            TrackerAnalysis = trackerSummary,
            HealthScore = healthScore,
            AnalyzedAt = DateTime.UtcNow,
        });
    }

    public Task<AiSearchParameters> ProcessNaturalLanguageSearchAsync(string naturalQuery)
    {
        if (string.IsNullOrWhiteSpace(naturalQuery))
        {
            return Task.FromResult(new AiSearchParameters
            {
                RawQuery = naturalQuery ?? string.Empty,
                CleanQuery = string.Empty,
                CleanTitle = string.Empty,
                ConfidenceScore = 0.0,
            });
        }

        var result = new AiSearchParameters
        {
            RawQuery = naturalQuery,
        };

        var working = naturalQuery.Trim();

        // Infer category from raw query first
        if (Regex.IsMatch(naturalQuery, @"(?i)\b(tv|series|season|episode|episodes|show|s\d+)\b"))
        {
            result.Category = "tv";
        }
        else if (Regex.IsMatch(naturalQuery, @"(?i)\b(movie|movies|film|cinema|remux|bluray|bdrip|brrip)\b"))
        {
            result.Category = "movies";
        }
        else if (Regex.IsMatch(naturalQuery, @"(?i)\b(album|music|flac|mp3|discography|soundtrack|song)\b"))
        {
            result.Category = "music";
        }
        else if (Regex.IsMatch(naturalQuery, @"(?i)\b(anime|manga)\b"))
        {
            result.Category = "anime";
        }
        else if (Regex.IsMatch(naturalQuery, @"(?i)\b(software|app|iso|linux|windows|macos)\b"))
        {
            result.Category = "software";
        }

        // 1. Freeleech
        if (Regex.IsMatch(working, @"(?i)\b(?:freeleech|free\s*leech)\b"))
        {
            result.FreeleechOnly = true;
            working = Regex.Replace(working, @"(?i)\b(?:freeleech|free\s*leech)\b", " ");
        }

        // 2. Min Seeders
        var seedersMatch = Regex.Match(working, @"(?i)(?:at\s*least|min|minimum|>=|>)\s*(?<seeds>\d+)\s*(?:seeders?|seeds?)");
        if (seedersMatch.Success)
        {
            result.MinSeeders = int.Parse(seedersMatch.Groups["seeds"].Value, CultureInfo.InvariantCulture);
            working = working.Remove(seedersMatch.Index, seedersMatch.Length);
        }
        else
        {
            var plusSeedsMatch = Regex.Match(working, @"(?i)(?<seeds>\d+)\+\s*(?:seeders?|seeds?)");
            if (plusSeedsMatch.Success)
            {
                result.MinSeeders = int.Parse(plusSeedsMatch.Groups["seeds"].Value, CultureInfo.InvariantCulture);
                working = working.Remove(plusSeedsMatch.Index, plusSeedsMatch.Length);
            }
        }

        // 3. Max Age Days
        var ageMatch = Regex.Match(working, @"(?i)(?:last|within|newer\s*than)\s*(?<days>\d+)\s*days?");
        if (ageMatch.Success)
        {
            result.MaxAgeDays = int.Parse(ageMatch.Groups["days"].Value, CultureInfo.InvariantCulture);
            working = working.Remove(ageMatch.Index, ageMatch.Length);
        }

        // 4. Resolution
        var resMatch = ResolutionRegex.Match(working);
        if (resMatch.Success)
        {
            var res = resMatch.Groups["res"].Value;
            result.Resolution = res.Equals("4k", StringComparison.OrdinalIgnoreCase) ? "2160p" : res.ToLowerInvariant();
            working = working.Remove(resMatch.Index, resMatch.Length);
        }

        // 5. Quality
        var qualityMatch = QualityRegex.Match(working);
        if (qualityMatch.Success)
        {
            result.Quality = NormalizeQuality(qualityMatch.Groups["quality"].Value);
            working = working.Remove(qualityMatch.Index, qualityMatch.Length);
        }

        // 6. Codec
        var codecMatch = VideoCodecRegex.Match(working);
        if (codecMatch.Success)
        {
            result.Codec = NormalizeCodec(codecMatch.Groups["codec"].Value);
            working = working.Remove(codecMatch.Index, codecMatch.Length);
        }

        // 7. Season & Episode
        var seMatch = SeasonEpisodeRegex.Match(working);
        if (seMatch.Success)
        {
            result.Season = int.Parse(seMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
            result.Episode = int.Parse(seMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
            result.Category = "tv";
            working = working.Remove(seMatch.Index, seMatch.Length);
        }
        else
        {
            var seasonMatch = SeasonOnlyRegex.Match(working);
            if (seasonMatch.Success)
            {
                result.Season = int.Parse(seasonMatch.Groups["season"].Value, CultureInfo.InvariantCulture);
                result.Category = "tv";
                working = working.Remove(seasonMatch.Index, seasonMatch.Length);
            }

            var epMatch = EpisodeOnlyRegex.Match(working);
            if (epMatch.Success)
            {
                result.Episode = int.Parse(epMatch.Groups["episode"].Value, CultureInfo.InvariantCulture);
                result.Category = "tv";
                working = working.Remove(epMatch.Index, epMatch.Length);
            }
        }

        // 8. Year
        var yearMatch = YearRegex.Match(working);
        if (yearMatch.Success)
        {
            result.Year = int.Parse(yearMatch.Groups["year"].Value, CultureInfo.InvariantCulture);
            working = working.Remove(yearMatch.Index, yearMatch.Length);
        }

        // 9. Default category if resolution/quality present and not specified
        if (string.IsNullOrEmpty(result.Category) && (!string.IsNullOrEmpty(result.Resolution) || !string.IsNullOrEmpty(result.Quality)))
        {
            result.Category = "movies";
        }

        // 10. Clean Title Extraction
        var clean = Regex.Replace(working, @"(?i)\b(download|find|search\s*for|search|grab|get|with|in|from|by|for|the|and)\b", " ");
        clean = Regex.Replace(clean, @"[^\w\s\-\.]", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        result.CleanTitle = clean;
        result.CleanQuery = string.IsNullOrWhiteSpace(clean) ? naturalQuery : clean;
        result.ConfidenceScore = 0.9;

        return Task.FromResult(result);
    }

    public Task<AiMalwareRiskAssessment> AnalyzeMalwareRiskAsync(string torrentName, IReadOnlyList<TorrentFile> files)
    {
        torrentName ??= string.Empty;
        files ??= Array.Empty<TorrentFile>();

        var suspiciousFiles = new List<string>();
        var threatReasons = new List<string>();
        var recommendations = new List<string>();
        var riskScore = 0.0;

        var totalFiles = files.Count;
        var hasMediaFiles = files.Any(f => MediaExtensions.Contains(Path.GetExtension(f.Path ?? string.Empty)));

        foreach (var file in files)
        {
            var filePath = file.Path ?? string.Empty;
            var ext = Path.GetExtension(filePath);
            var fileName = Path.GetFileName(filePath);

            // 1. Executable or script in media release
            if (DangerousExtensions.Contains(ext))
            {
                suspiciousFiles.Add(filePath);

                if (hasMediaFiles || Regex.IsMatch(torrentName, @"(?i)\b(1080p|2160p|720p|bluray|web-dl|flac|mp3)\b"))
                {
                    riskScore += 0.6;
                    threatReasons.Add($"Dangerous executable/script '{fileName}' found in a media release.");
                }
                else
                {
                    riskScore += 0.3;
                    threatReasons.Add($"Executable/script binary '{fileName}' found.");
                }
            }

            // 2. Double extension trick (e.g. Movie.mp4.exe, Song.mp3.scr)
            if (Regex.IsMatch(fileName, @"(?i)\.(mkv|mp4|avi|mp3|flac|pdf|jpg|png)\.(exe|scr|bat|cmd|vbs|js|pif|ps1|msi)$"))
            {
                riskScore += 0.8;
                suspiciousFiles.Add(filePath);
                threatReasons.Add($"High-risk double extension masquerade detected on '{fileName}'.");
            }

            // 3. Fake codec / password unlocker traps
            if (Regex.IsMatch(fileName, @"(?i)(password|unlocker|codec_setup|install_first|how_to_play|readme_key)\.(exe|scr|url|lnk|bat)"))
            {
                riskScore += 0.7;
                suspiciousFiles.Add(filePath);
                threatReasons.Add($"Potential fake codec or archive password trap found: '{fileName}'.");
            }

            // 4. Suspiciously tiny media files (< 500 KB for video)
            if (MediaExtensions.Contains(ext) && file.Size > 0 && file.Size < 500 * 1024)
            {
                if (Regex.IsMatch(torrentName, @"(?i)\b(1080p|2160p|720p|bluray|web-dl|movie)\b"))
                {
                    riskScore += 0.25;
                    threatReasons.Add($"Media file '{fileName}' has suspiciously low file size ({file.Size / 1024} KB).");
                }
            }
        }

        // Check torrent name anomalies
        if (Regex.IsMatch(torrentName, @"(?i)\.(exe|scr|bat|cmd)$"))
        {
            riskScore += 0.5;
            threatReasons.Add("Torrent payload itself is named as a standalone executable/script.");
        }

        riskScore = Math.Clamp(riskScore, 0.0, 1.0);

        string riskLevel;
        if (riskScore >= 0.8)
        {
            riskLevel = "Critical";
            recommendations.Add("Do NOT open or execute files from this torrent. Delete immediately.");
        }
        else if (riskScore >= 0.5)
        {
            riskLevel = "High";
            recommendations.Add("Uncheck suspicious executables before downloading. Scan with anti-virus.");
        }
        else if (riskScore >= 0.25)
        {
            riskLevel = "Medium";
            recommendations.Add("Verify file extensions carefully before opening.");
        }
        else if (riskScore > 0.05)
        {
            riskLevel = "Low";
            recommendations.Add("Minor warnings found. Proceed with standard caution.");
        }
        else
        {
            riskLevel = "Safe";
            recommendations.Add("No anomalous or malicious patterns detected.");
        }

        return Task.FromResult(new AiMalwareRiskAssessment
        {
            TorrentName = torrentName,
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            IsSuspicious = riskScore >= 0.5,
            AnalyzedFilesCount = totalFiles,
            SuspiciousFileNames = suspiciousFiles.Distinct().ToList(),
            ThreatReasons = threatReasons.Distinct().ToList(),
            Recommendations = recommendations,
            AssessedAt = DateTime.UtcNow,
        });
    }

    public Task<string> GenerateChatResponseAsync(string userMessage, string systemContext = null)
    {
        userMessage ??= string.Empty;
        var lower = userMessage.ToLowerInvariant();

        var response = new StringBuilder();

        if (lower.Contains("ratio") || lower.Contains("seed"))
        {
            response.AppendLine("### BitTorrent Seeding & Ratio Guide");
            response.AppendLine("- **Share Ratio:** Uploaded bytes divided by downloaded bytes. A ratio &ge; 1.0 means you have uploaded as much data as you received.");
            response.AppendLine("- **Target Ratio:** You can configure per-category or global ratio goals in Leecharr Settings.");
            response.AppendLine("- **Initial Seeding (Super-Seeding):** Efficiently distributes unique pieces when you are the only seeder.");
        }
        else if (lower.Contains("vpn") || lower.Contains("kill switch") || lower.Contains("interface"))
        {
            response.AppendLine("### VPN & Network Binding");
            response.AppendLine("- **Kill Switch:** When bound to a VPN interface (`tun0`, `wg0`), Leecharr immediately terminates all peer traffic if the VPN disconnects.");
            response.AppendLine("- **SOCKS5 Proxy:** Optional proxying for tracker announces and peer connections.");
        }
        else if (lower.Contains("sonarr") || lower.Contains("radarr") || lower.Contains("prowlarr") || lower.Contains("servarr"))
        {
            response.AppendLine("### Servarr (*arr) Integration");
            response.AppendLine("- Leecharr offers native drop-in compatibility for **qBittorrent WebAPI v2** (`/api/v2/*`) and **Transmission RPC** (`/transmission/rpc`) on port 7889.");
            response.AppendLine("- Connect Sonarr/Radarr directly using `localhost:7889` as a qBittorrent or Transmission client.");
        }
        else if (lower.Contains("slow") || lower.Contains("stalled") || lower.Contains("health"))
        {
            response.AppendLine("### Diagnostics & Speed Troubleshooting");
            response.AppendLine("- Check that your listening port (default `51413`) is forwarded via UPnP or NAT router rules.");
            response.AppendLine("- Verify that the torrent swarm has active seeders (> 0).");
            response.AppendLine("- Check Tracker status tab to verify announce responses.");
        }
        else
        {
            response.AppendLine($"Leecharr AI Copilot operational. Ready to assist with release parsing, swarm diagnostics, search queries, and Servarr integration for '{userMessage}'.");
        }

        return Task.FromResult(response.ToString());
    }

    private static string ExtractCleanTitle(string raw, AiParsedRelease parsed)
    {
        var clean = raw;

        // Strip release group suffix
        if (!string.IsNullOrEmpty(parsed.ReleaseGroup))
        {
            clean = Regex.Replace(clean, @"-(?i)" + Regex.Escape(parsed.ReleaseGroup) + @"$", string.Empty);
            clean = Regex.Replace(clean, @"\[(?i)" + Regex.Escape(parsed.ReleaseGroup) + @"\]$", string.Empty);
        }

        // Find the earliest index of technical tags
        var cutoffIndex = clean.Length;

        void CheckMatch(Regex regex)
        {
            var m = regex.Match(clean);
            if (m.Success && m.Index > 0 && m.Index < cutoffIndex)
            {
                cutoffIndex = m.Index;
            }
        }

        CheckMatch(SeasonEpisodeRegex);
        CheckMatch(AltSeasonEpisodeRegex);
        CheckMatch(SeasonOnlyRegex);
        CheckMatch(ResolutionRegex);
        CheckMatch(QualityRegex);
        CheckMatch(VideoCodecRegex);

        if (parsed.Year.HasValue)
        {
            var yearMatches = YearRegex.Matches(clean);
            foreach (Match ym in yearMatches)
            {
                if (ym.Index > 0 && ym.Index < cutoffIndex)
                {
                    cutoffIndex = ym.Index;
                    break;
                }
            }
        }

        if (cutoffIndex < clean.Length)
        {
            clean = clean[..cutoffIndex];
        }

        // Replace dots, underscores, dashes with spaces
        clean = Regex.Replace(clean, @"[\._\+]", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(clean) ? raw : clean;
    }

    private static string NormalizeQuality(string quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return quality;
        }

        var q = quality.ToUpperInvariant();
        if (q.Contains("BLURAY") || q.Contains("BRRIP") || q.Contains("BDRIP"))
        {
            return "BluRay";
        }

        if (q.Contains("WEB-DL") || q.Contains("WEBDL"))
        {
            return "WEB-DL";
        }

        if (q.Contains("WEBRIP"))
        {
            return "WEBRip";
        }

        if (q.Contains("HDTV"))
        {
            return "HDTV";
        }

        if (q.Contains("DVDRIP") || q.Contains("DVD-R"))
        {
            return "DVDRip";
        }

        if (q.Contains("REMUX"))
        {
            return "REMUX";
        }

        if (q.Contains("CAM"))
        {
            return "CAM";
        }

        if (q.Contains("TS") || q.Contains("TELESYNC"))
        {
            return "TeleSync";
        }

        return quality;
    }

    private static string NormalizeCodec(string codec)
    {
        if (string.IsNullOrWhiteSpace(codec))
        {
            return codec;
        }

        var c = codec.ToUpperInvariant();
        if (c.Contains("X265") || c.Contains("HEVC") || c.Contains("H.265") || c.Contains("H265"))
        {
            return "x265";
        }

        if (c.Contains("X264") || c.Contains("AVC") || c.Contains("H.264") || c.Contains("H264"))
        {
            return "x264";
        }

        if (c.Contains("AV1"))
        {
            return "AV1";
        }

        if (c.Contains("XVID"))
        {
            return "XviD";
        }

        if (c.Contains("DIVX"))
        {
            return "DivX";
        }

        return codec;
    }

    private static string NormalizeAudioCodec(string audio)
    {
        if (string.IsNullOrWhiteSpace(audio))
        {
            return audio;
        }

        var a = audio.ToUpperInvariant();
        if (a.Contains("DTS-HD") && a.Contains("MA"))
        {
            return "DTS-HD MA";
        }

        if (a.Contains("TRUEHD") && a.Contains("ATMOS"))
        {
            return "TRUEHD ATMOS";
        }

        if (a.Contains("TRUEHD"))
        {
            return "TrueHD";
        }

        if (a.Contains("ATMOS"))
        {
            return "Atmos";
        }

        if (a.Contains("DTS-HD"))
        {
            return "DTS-HD";
        }

        if (a.Contains("DD5.1") || a.Contains("DD5 1"))
        {
            return "DD5.1";
        }

        if (a.Contains("EAC3") || a.Contains("E-AC-3") || a.Contains("DDP"))
        {
            return "EAC3";
        }

        if (a.Contains("AC3") || a.Contains("AC-3"))
        {
            return "AC3";
        }

        return audio.ToUpperInvariant();
    }

    private static string NormalizeHdr(string hdr)
    {
        if (string.IsNullOrWhiteSpace(hdr))
        {
            return hdr;
        }

        var h = hdr.ToUpperInvariant();
        if (h.Contains("DV") || h.Contains("DOLBY VISION"))
        {
            return "Dolby Vision";
        }

        if (h.Contains("HDR10+"))
        {
            return "HDR10+";
        }

        if (h.Contains("HDR10"))
        {
            return "HDR10";
        }

        if (h.Contains("HDR"))
        {
            return "HDR";
        }

        return hdr;
    }
}
