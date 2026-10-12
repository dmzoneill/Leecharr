// Copyright (c) FeedItOut. All rights reserved.

using System;
using NUnit.Framework;
using NzbDrone.Core.Developer.Uml;

namespace Leecharr.Core.Test.Developer;

[TestFixture]
public class DeveloperUmlServiceTest
{
    private DeveloperUmlService _service;

    [SetUp]
    public void SetUp()
    {
        _service = new DeveloperUmlService();
    }

    [Test]
    public void GetAvailableSubsystems_returns_expected_subsystems()
    {
        var subsystems = _service.GetAvailableSubsystems();

        Assert.That(subsystems, Is.Not.Empty);
        Assert.That(subsystems, Contains.Item("all"));
        Assert.That(subsystems, Contains.Item("torrents"));
        Assert.That(subsystems, Contains.Item("trackers"));
        Assert.That(subsystems, Contains.Item("indexers"));
    }

    [Test]
    public void GetAvailableDiagramTypes_returns_expected_types()
    {
        var types = _service.GetAvailableDiagramTypes();

        Assert.That(types, Is.Not.Empty);
        Assert.That(types, Contains.Item("class"));
        Assert.That(types, Contains.Item("di"));
        Assert.That(types, Contains.Item("state"));
        Assert.That(types, Contains.Item("api"));
        Assert.That(types, Contains.Item("frontend"));
    }

    [TestCase("class", "torrents")]
    [TestCase("class", "all")]
    [TestCase("di", "torrents")]
    [TestCase("state", "torrents")]
    [TestCase("state", "trackers")]
    [TestCase("state", "authentication")]
    [TestCase("api", "all")]
    [TestCase("api", "authentication")]
    [TestCase("frontend", "all")]
    public void GenerateDiagram_produces_valid_mermaid_markup(string diagramType, string subsystem)
    {
        var options = new DeveloperUmlOptions
        {
            DiagramType = diagramType,
            Subsystem = subsystem
        };

        var result = _service.GenerateDiagram(options);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.DiagramType, Is.EqualTo(diagramType));
        Assert.That(result.MermaidCode, Is.Not.Null);
        Assert.That(result.MermaidCode.Length, Is.GreaterThan(10));
        Assert.That(result.NodeCount, Is.GreaterThan(0));

        if (diagramType == "class")
        {
            Assert.That(result.MermaidCode, Does.StartWith("classDiagram"));
        }
        else if (diagramType == "di")
        {
            Assert.That(result.MermaidCode, Does.StartWith("graph TD"));
        }
        else if (diagramType == "state")
        {
            Assert.That(result.MermaidCode, Does.StartWith("stateDiagram-v2"));
        }
        else if (diagramType == "api")
        {
            Assert.That(result.MermaidCode, Does.StartWith("graph LR"));
        }
        else if (diagramType == "frontend")
        {
            Assert.That(result.MermaidCode, Does.StartWith("graph TD"));
        }
    }

    [Test]
    public void GenerateStateDiagram_uses_subsystem_specific_states()
    {
        var torrent = _service.GenerateDiagram(new DeveloperUmlOptions { DiagramType = "state", Subsystem = "torrents" });
        var auth = _service.GenerateDiagram(new DeveloperUmlOptions { DiagramType = "state", Subsystem = "authentication" });

        Assert.That(torrent.MermaidCode, Does.Contain("Queued"));
        Assert.That(auth.MermaidCode, Does.Contain("Authenticating"));
        Assert.That(auth.MermaidCode, Does.Not.Contain("Queued --> Downloading"));
        Assert.That(torrent.EdgeCount, Is.EqualTo(10));
        Assert.That(torrent.NodeCount, Is.EqualTo(8));
        Assert.That(auth.NodeCount, Is.EqualTo(6));
        Assert.That(auth.EdgeCount, Is.EqualTo(10));
    }

    [Test]
    public void GenerateApiTopologyDiagram_filters_controllers_by_subsystem()
    {
        var all = _service.GenerateDiagram(new DeveloperUmlOptions { DiagramType = "api", Subsystem = "all" });
        var auth = _service.GenerateDiagram(new DeveloperUmlOptions { DiagramType = "api", Subsystem = "authentication" });

        Assert.That(auth.MermaidCode, Does.Contain("AuthController"));
        Assert.That(auth.MermaidCode, Does.Not.Contain("TorrentEngineController"));
        Assert.That(auth.NodeCount, Is.LessThan(all.NodeCount));
    }

    [Test]
    public void GenerateDiagram_rejects_unknown_subsystem()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.GenerateDiagram(new DeveloperUmlOptions
        {
            DiagramType = "class",
            Subsystem = "torrent"
        }));
    }
}
