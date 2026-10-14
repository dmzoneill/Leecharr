// Copyright (c) FeedItOut. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NzbDrone.Core.Torrents;

namespace NzbDrone.Core.Developer.Uml;

public class DeveloperUmlService : IDeveloperUmlService
{
    private static readonly string[] Subsystems =
    [
        "all",
        "torrents",
        "trackers",
        "indexers",
        "authentication",
        "automation",
        "notifications",
        "network",
        "storage"
    ];

    private static readonly string[] DiagramTypes =
    [
        "class",
        "di",
        "state",
        "api",
        "frontend"
    ];

    public List<string> GetAvailableSubsystems() => Subsystems.ToList();

    public List<string> GetAvailableDiagramTypes() => DiagramTypes.ToList();

    public DeveloperUmlDiagramResult GenerateDiagram(DeveloperUmlOptions options)
    {
        options ??= new DeveloperUmlOptions();
        var diagramType = (options.DiagramType ?? "class").ToLowerInvariant();
        var subsystem = (options.Subsystem ?? "all").ToLowerInvariant();

        if (!Subsystems.Contains(subsystem))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                subsystem,
                $"Unknown subsystem '{subsystem}'. Valid values: {string.Join(", ", Subsystems)}.");
        }

        return diagramType switch
        {
            "di" => GenerateDependencyInjectionDiagram(subsystem),
            "state" => GenerateStateDiagram(subsystem),
            "api" => GenerateApiTopologyDiagram(subsystem),
            "frontend" => GenerateFrontendDiagram(),
            _ => GenerateClassDiagram(subsystem, options.IncludeInterfaces, options.IncludeMethods)
        };
    }

    private static DeveloperUmlDiagramResult GenerateClassDiagram(string subsystem, bool includeInterfaces, bool includeMethods)
    {
        var sb = new StringBuilder();
        sb.AppendLine("classDiagram");

        var coreAssembly = typeof(Torrent).Assembly;
        var types = GetFilteredTypes(coreAssembly, subsystem);

        var nodeCount = 0;
        var edgeCount = 0;
        var renderedTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types.Take(25))
        {
            var typeName = SanitizeIdentifier(type.Name);
            renderedTypes.Add(type.Name);
            nodeCount++;

            sb.AppendLine($"    class {typeName} {{");
            if (type.IsInterface)
            {
                sb.AppendLine("        <<interface>>");
            }
            else if (type.IsAbstract)
            {
                sb.AppendLine("        <<abstract>>");
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Take(6);
            foreach (var prop in props)
            {
                sb.AppendLine($"        +{SanitizeIdentifier(prop.PropertyType.Name)} {SanitizeIdentifier(prop.Name)}");
            }

            if (includeMethods)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName)
                    .Take(4);

                foreach (var method in methods)
                {
                    sb.AppendLine($"        +{SanitizeIdentifier(method.Name)}()");
                }
            }

            sb.AppendLine("    }");

            if (type.BaseType != null && type.BaseType != typeof(object) && types.Contains(type.BaseType))
            {
                sb.AppendLine($"    {SanitizeIdentifier(type.BaseType.Name)} <|-- {typeName}");
                edgeCount++;
            }

            if (includeInterfaces)
            {
                foreach (var iface in type.GetInterfaces())
                {
                    if (types.Contains(iface))
                    {
                        sb.AppendLine($"    {SanitizeIdentifier(iface.Name)} <|.. {typeName}");
                        edgeCount++;
                    }
                }
            }
        }

        return new DeveloperUmlDiagramResult
        {
            DiagramType = "class",
            Subsystem = subsystem,
            Title = $"UML Class Diagram — {subsystem.ToUpperInvariant()}",
            MermaidCode = sb.ToString(),
            NodeCount = nodeCount,
            EdgeCount = edgeCount
        };
    }

    private static DeveloperUmlDiagramResult GenerateDependencyInjectionDiagram(string subsystem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");

        var coreAssembly = typeof(Torrent).Assembly;
        var types = GetFilteredTypes(coreAssembly, subsystem)
            .Where(t => !t.IsInterface && !t.IsAbstract)
            .ToList();

        var nodeCount = 0;
        var edgeCount = 0;
        var registeredNodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var type in types.Take(20))
        {
            var constructors = type.GetConstructors();
            if (constructors.Length == 0) continue;

            var ctor = constructors.OrderByDescending(c => c.GetParameters().Length).First();
            var targetId = SanitizeIdentifier(type.Name);

            if (registeredNodes.Add(targetId))
            {
                sb.AppendLine($"    {targetId}[\"{type.Name}\"]");
                nodeCount++;
            }

            foreach (var param in ctor.GetParameters())
            {
                var paramType = param.ParameterType;
                if (paramType.IsInterface)
                {
                    var sourceId = SanitizeIdentifier(paramType.Name);
                    if (registeredNodes.Add(sourceId))
                    {
                        sb.AppendLine($"    {sourceId}[\"{paramType.Name}\"]");
                        nodeCount++;
                    }

                    sb.AppendLine($"    {targetId} -->|injects| {sourceId}");
                    edgeCount++;
                }
            }
        }

        return new DeveloperUmlDiagramResult
        {
            DiagramType = "di",
            Subsystem = subsystem,
            Title = $"Dependency Injection Topology — {subsystem.ToUpperInvariant()}",
            MermaidCode = sb.ToString(),
            NodeCount = nodeCount,
            EdgeCount = edgeCount
        };
    }

    private static DeveloperUmlDiagramResult GenerateStateDiagram(string subsystem)
    {
        var body = subsystem switch
        {
            "trackers" => """
                [*] --> Unknown
                Unknown --> Announcing: Initial Announce
                Announcing --> Working: HTTP/UDP 200 Received
                Announcing --> Failed: Timeout / Connection Refused
                Working --> Announcing: Interval Reached
                Working --> Scraping: Scrape Cycle
                Scraping --> Working: Seeders/Leechers Updated
                Failed --> Announcing: Retry Backoff Interval
                Working --> Disabled: User Toggled Off
                Disabled --> Announcing: User Enabled
                """,
            "torrents" => """
                [*] --> Queued: Download Queued / Link Added
                Queued --> Downloading: Engine Allocates Stream
                Downloading --> Checking: Pieces Verification
                Checking --> Seeding: Hash Integrity Passed
                Downloading --> Paused: User Pause / Speed Schedule
                Paused --> Downloading: User Resume
                Downloading --> Error: Disk Fault / Transport Failure
                Error --> Queued: Auto-Retry Recovered
                Seeding --> Stopped: Complete
                Stopped --> [*]: Removed
                """,
            "indexers" => """
                [*] --> Idle
                Idle --> Fetching: RSS / Torznab Poll
                Fetching --> Parsing: HTTP 200
                Fetching --> Backoff: Timeout / 429
                Parsing --> Ready: Releases Indexed
                Parsing --> Backoff: Parse Error
                Backoff --> Fetching: Cooldown Elapsed
                Ready --> Fetching: Sync Interval
                Ready --> Disabled: User Disabled
                Disabled --> Idle: Re-enabled
                """,
            "authentication" => """
                [*] --> Anonymous
                Anonymous --> Authenticating: Login / API Key
                Authenticating --> Authenticated: Credentials Valid
                Authenticating --> Failed: Invalid Credentials
                Failed --> Authenticating: Retry
                Authenticated --> Expired: Session TTL
                Expired --> Authenticating: Refresh Token
                Authenticated --> Anonymous: Logout
                Failed --> Locked: Brute Force Threshold
                Locked --> Anonymous: Admin Unlock
                """,
            "automation" => """
                [*] --> Idle
                Idle --> Armed: Script Enabled
                Armed --> Executing: Torrent Event Fired
                Executing --> Completed: Script Exit 0
                Executing --> Failed: Script Error / Timeout
                Failed --> Armed: Manual Reset
                Completed --> Armed: Next Event
                Armed --> Idle: Script Disabled
                """,
            "notifications" => """
                [*] --> Queued
                Queued --> Rendering: Template Build
                Rendering --> Sending: Provider Dispatch
                Sending --> Delivered: HTTP 2xx
                Sending --> Retrying: Transient Failure
                Retrying --> Sending: Backoff Elapsed
                Sending --> Failed: Max Retries
                Failed --> Queued: Manual Resend
                Delivered --> [*]
                """,
            "network" => """
                [*] --> Offline
                Offline --> Resolving: DNS Lookup
                Resolving --> Connecting: Socket Open
                Connecting --> Online: TLS / Handshake OK
                Connecting --> Blocked: Firewall / BIND Deny
                Online --> Throttled: Rate Limit
                Throttled --> Online: Window Reset
                Online --> Offline: Interface Down
                Blocked --> Offline: Policy Reset
                """,
            "storage" => """
                [*] --> Scanning
                Scanning --> Available: Free Space OK
                Scanning --> LowSpace: Threshold Crossed
                LowSpace --> Critical: Quota Exceeded
                Critical --> Quarantine: Auto-Quarantine
                Available --> Relocating: Path Migration
                Relocating --> Available: Move Complete
                LowSpace --> Available: Cleanup Freed Space
                Critical --> Available: Emergency Purge
                Quarantine --> Available: Manual Release
                """,
            "all" => """
                [*] --> Bootstrapping
                Bootstrapping --> Running: Host Started
                Running --> Degraded: Subsystem Fault
                Degraded --> Running: Recovery
                Running --> Maintenance: Config Reload
                Maintenance --> Running: Reload Complete
                Running --> ShuttingDown: Stop Signal
                ShuttingDown --> [*]
                """,
            _ => string.Empty
        };

        var transitions = string.IsNullOrEmpty(body)
            ? Array.Empty<string>()
            : body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return BuildStateDiagramResult(subsystem, transitions);
    }

    private static DeveloperUmlDiagramResult GenerateApiTopologyDiagram(string subsystem)
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph LR");
        sb.AppendLine("    Client[\"Frontend / API Clients\"] --> Gateway[\"ASP.NET Core Routing\"]");

        var apiAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name?.Contains("Api.V1") == true)
            ?? typeof(Torrent).Assembly;

        var controllers = apiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal) && !t.IsAbstract)
            .Where(t => MatchesApiSubsystem(t, subsystem))
            .OrderBy(t => t.Name)
            .Take(15)
            .ToList();

        var nodeCount = 2;
        var edgeCount = 1;

        foreach (var c in controllers)
        {
            var cName = SanitizeIdentifier(c.Name.Replace("Controller", string.Empty));
            sb.AppendLine($"    Gateway --> {cName}[\"{c.Name}\"]");
            nodeCount++;
            edgeCount++;
        }

        return new DeveloperUmlDiagramResult
        {
            DiagramType = "api",
            Subsystem = subsystem,
            Title = $"API Controller Route Topology — {subsystem.ToUpperInvariant()}",
            MermaidCode = sb.ToString(),
            NodeCount = nodeCount,
            EdgeCount = edgeCount
        };
    }

    private static DeveloperUmlDiagramResult GenerateFrontendDiagram()
    {
        var sb = new StringBuilder();
        sb.AppendLine("graph TD");
        sb.AppendLine("    App[\"App.tsx\"] --> Router[\"Tab Navigation Router\"]");
        sb.AppendLine("    App --> ThemeContext[\"ThemeContext\"]");
        sb.AppendLine("    App --> ConfirmContext[\"ConfirmContext\"]");
        sb.AppendLine("    App --> SignalR[\"SignalRProvider\"]");
        sb.AppendLine("    Router --> Dashboard[\"Dashboard.tsx\"]");
        sb.AppendLine("    Router --> TorrentIndex[\"TorrentIndex.tsx\"]");
        sb.AppendLine("    Router --> Settings[\"Settings.tsx\"]");
        sb.AppendLine("    Router --> Developer[\"Developer Pages\"]");
        sb.AppendLine("    Developer --> DevQuality[\"DeveloperQuality.tsx\"]");
        sb.AppendLine("    Developer --> DevUml[\"DeveloperUml.tsx\"]");
        sb.AppendLine("    Developer --> DevPRs[\"DeveloperPullRequests.tsx\"]");
        sb.AppendLine("    Developer --> DevIssues[\"DeveloperIssues.tsx\"]");

        return new DeveloperUmlDiagramResult
        {
            DiagramType = "frontend",
            Subsystem = "frontend",
            Title = "Frontend Component & Context Topology",
            MermaidCode = sb.ToString(),
            NodeCount = 12,
            EdgeCount = 11
        };
    }

    private static List<Type> GetFilteredTypes(Assembly assembly, string subsystem)
    {
        var allTypes = assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsNested && !t.IsEnum)
            .ToList();

        if (subsystem == "all")
        {
            return allTypes.Where(t => t.Name.EndsWith("Service") || t.Name.EndsWith("Repository") || t.IsInterface)
                .OrderBy(t => t.Name)
                .ToList();
        }

        return allTypes.Where(t => MatchesSubsystem(t, subsystem))
            .OrderBy(t => t.Name)
            .ToList();
    }

    private static bool MatchesSubsystem(Type type, string subsystem)
    {
        var ns = type.Namespace ?? string.Empty;
        var name = type.Name;

        return subsystem switch
        {
            "torrents" => ns.Contains("Torrent") || name.Contains("Torrent") || name.Contains("Engine"),
            "trackers" => ns.Contains("Tracker") || name.Contains("Tracker"),
            "indexers" => ns.Contains("Indexer") || name.Contains("Indexer") || name.Contains("Rss"),
            "authentication" => ns.Contains("Auth") || name.Contains("Auth") || name.Contains("User"),
            "automation" => ns.Contains("Automation") || name.Contains("Script"),
            "notifications" => ns.Contains("Notification") || name.Contains("Webhook"),
            "network" => ns.Contains("Network") || ns.Contains("Binding") || name.Contains("Ip"),
            "storage" => ns.Contains("Storage") || ns.Contains("Datastore") || name.Contains("Disk")
                || ns.Contains("FileBrowser") || ns.Contains("DiskSpace") || ns.Contains("Backup"),
            _ => false
        };
    }

    private static bool MatchesApiSubsystem(Type controller, string subsystem)
    {
        if (subsystem == "all")
        {
            return true;
        }

        if (MatchesSubsystem(controller, subsystem))
        {
            return true;
        }

        var ns = controller.Namespace ?? string.Empty;

        return subsystem switch
        {
            "torrents" => ns.Contains(".BitTorrent") || ns.Contains(".Torrents") || ns.Contains(".Seeding")
                || ns.Contains(".Peers") || ns.Contains(".Deluge") || ns.Contains(".QBittorrent")
                || ns.Contains(".RTorrent") || ns.Contains(".Hadouken") || ns.Contains(".Flood")
                || ns.Contains(".Aria2") || ns.Contains(".Freebox") || ns.Contains(".Sabnzbd")
                || ns.Contains(".Nzbget") || ns.Contains(".DownloadClients"),
            "trackers" => ns.Contains(".Tracker"),
            "indexers" => ns.Contains(".Indexers"),
            "authentication" => ns.Contains(".Auth"),
            "automation" => ns.Contains(".Automation"),
            "notifications" => ns.Contains(".Notifications"),
            "network" => ns.Contains(".Network"),
            "storage" => ns.Contains(".DiskSpace") || ns.Contains(".FileBrowser") || ns.Contains(".Backup"),
            _ => false
        };
    }

    private static DeveloperUmlDiagramResult BuildStateDiagramResult(string subsystem, string[] transitions)
    {
        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");
        foreach (var transition in transitions)
        {
            sb.AppendLine($"    {transition}");
        }

        var mermaidCode = sb.ToString();
        var (nodeCount, edgeCount) = MeasureStateDiagram(mermaidCode);

        return new DeveloperUmlDiagramResult
        {
            DiagramType = "state",
            Subsystem = subsystem,
            Title = $"Lifecycle State Machine — {subsystem.ToUpperInvariant()}",
            MermaidCode = mermaidCode,
            NodeCount = nodeCount,
            EdgeCount = edgeCount
        };
    }

    private static (int NodeCount, int EdgeCount) MeasureStateDiagram(string mermaidCode)
    {
        var states = new HashSet<string>(StringComparer.Ordinal);
        var edgeCount = 0;

        foreach (var rawLine in mermaidCode.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.Contains("-->", StringComparison.Ordinal))
            {
                continue;
            }

            edgeCount++;
            var parts = line.Split("-->", 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            var from = ExtractStateName(parts[0]);
            var to = ExtractStateName(parts[1]);
            if (!string.IsNullOrEmpty(from))
            {
                states.Add(from);
            }

            if (!string.IsNullOrEmpty(to))
            {
                states.Add(to);
            }
        }

        return (states.Count, edgeCount);
    }

    private static string ExtractStateName(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token == "[*]")
        {
            return null;
        }

        var colonIndex = token.IndexOf(':');
        if (colonIndex >= 0)
        {
            token = token[..colonIndex].Trim();
        }

        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        return new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }
}
