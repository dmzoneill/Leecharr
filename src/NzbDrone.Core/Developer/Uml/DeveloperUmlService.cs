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
        var sb = new StringBuilder();
        sb.AppendLine("stateDiagram-v2");

        if (subsystem == "trackers")
        {
            sb.AppendLine("    [*] --> Unknown");
            sb.AppendLine("    Unknown --> Announcing: Initial Announce");
            sb.AppendLine("    Announcing --> Working: HTTP/UDP 200 Received");
            sb.AppendLine("    Announcing --> Failed: Timeout / Connection Refused");
            sb.AppendLine("    Working --> Announcing: Interval Reached");
            sb.AppendLine("    Working --> Scraping: Scrape Cycle");
            sb.AppendLine("    Scraping --> Working: Seeders/Leechers Updated");
            sb.AppendLine("    Failed --> Announcing: Retry Backoff Interval");
            sb.AppendLine("    Working --> Disabled: User Toggled Off");
            sb.AppendLine("    Disabled --> Announcing: User Enabled");
        }
        else
        {
            sb.AppendLine("    [*] --> Queued: Download Queued / Link Added");
            sb.AppendLine("    Queued --> Downloading: Engine Allocates Stream");
            sb.AppendLine("    Downloading --> Checking: Pieces Verification");
            sb.AppendLine("    Checking --> Seeding: Hash Integrity Passed");
            sb.AppendLine("    Downloading --> Paused: User Pause / Speed Schedule");
            sb.AppendLine("    Paused --> Downloading: User Resume");
            sb.AppendLine("    Downloading --> Error: Disk Fault / Transport Failure");
            sb.AppendLine("    Error --> Queued: Auto-Retry Recovered");
            sb.AppendLine("    Seeding --> Stopped: Complete");
            sb.AppendLine("    Stopped --> [*]: Removed");
        }

        return new DeveloperUmlDiagramResult
        {
            DiagramType = "state",
            Subsystem = subsystem,
            Title = $"Lifecycle State Machine — {subsystem.ToUpperInvariant()}",
            MermaidCode = sb.ToString(),
            NodeCount = 8,
            EdgeCount = 10
        };
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
            Title = "API Controller Route Topology",
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
            "storage" => ns.Contains("Storage") || ns.Contains("Datastore") || name.Contains("Disk"),
            _ => true
        };
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        return new string(name.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
    }
}
