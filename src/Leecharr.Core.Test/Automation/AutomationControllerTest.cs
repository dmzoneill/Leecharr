// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Leecharr.Api.V1.Automation;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using NzbDrone.Core.Automation;
using NzbDrone.SignalR;

namespace Leecharr.Core.Test.Automation;

[TestFixture]
public class AutomationControllerTest
{
    private IAutomationService automationService = null!;
    private IAutomationMarketplaceService marketplaceService = null!;
    private IBroadcastSignalRMessage signalRBroadcaster = null!;
    private AutomationController controller = null!;

    [SetUp]
    public void SetUp()
    {
        this.automationService = Substitute.For<IAutomationService>();
        this.marketplaceService = Substitute.For<IAutomationMarketplaceService>();
        this.signalRBroadcaster = Substitute.For<IBroadcastSignalRMessage>();

        this.controller = new AutomationController(
            this.automationService,
            this.marketplaceService,
            this.signalRBroadcaster);
    }

    [Test]
    public void GetAll_ReturnsAllMappedScriptResources()
    {
        var scripts = new List<AutomationScript>
        {
            new()
            {
                Id = 1,
                Name = "Auto-Tag",
                Description = "Tag script",
                Trigger = AutomationTrigger.TorrentAdded,
                Language = AutomationLanguage.JavaScript,
                Code = "console.log('hi');",
                IsEnabled = true,
                TargetCategories = new List<string> { "movies" },
                TargetTagIds = new List<int> { 10 },
            },
            new()
            {
                Id = 2,
                Name = "Auto-Pause",
                Trigger = AutomationTrigger.TorrentCompleted,
                Language = AutomationLanguage.Yaml,
                Code = "trigger: TorrentCompleted",
                IsEnabled = false,
            },
        };
        this.automationService.GetAll().Returns(scripts);

        var result = this.controller.GetAll();

        result.Value.Should().NotBeNull();
        result.Value!.Count.Should().Be(2);

        result.Value[0].Id.Should().Be(1);
        result.Value[0].Name.Should().Be("Auto-Tag");
        result.Value[0].Trigger.Should().Be(AutomationTrigger.TorrentAdded);
        result.Value[0].Language.Should().Be(AutomationLanguage.JavaScript);
        result.Value[0].TargetCategories.Should().Contain("movies");
        result.Value[0].TargetTagIds.Should().Contain(10);

        result.Value[1].Id.Should().Be(2);
        result.Value[1].Name.Should().Be("Auto-Pause");
        result.Value[1].IsEnabled.Should().BeFalse();
        result.Value[1].TargetCategories.Should().BeEmpty();
        result.Value[1].TargetTagIds.Should().BeEmpty();
    }

    [Test]
    public void Get_WhenScriptExists_ReturnsResource()
    {
        var script = new AutomationScript
        {
            Id = 42,
            Name = "FindMe",
            Description = "Description 42",
            Trigger = AutomationTrigger.RatioReached,
            Language = AutomationLanguage.JavaScript,
        };
        this.automationService.Get(42).Returns(script);

        var result = this.controller.Get(42);

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(42);
        result.Value.Name.Should().Be("FindMe");
    }

    [Test]
    public void Get_WhenScriptDoesNotExist_ReturnsNotFound()
    {
        this.automationService.Get(999).Returns((AutomationScript)null!);

        var result = this.controller.Get(999);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Test]
    public void Create_WhenResourceIsNull_ReturnsBadRequest()
    {
        var result = this.controller.Create(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().Be("Request body cannot be null");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Create_WhenNameIsMissingOrWhitespace_ReturnsBadRequest(string name)
    {
        var resource = new AutomationScriptResource { Name = name };

        var result = this.controller.Create(resource);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().Be("Script name is required.");
    }

    [Test]
    public void Create_WhenValid_AddsScriptAndReturnsResource()
    {
        var resource = new AutomationScriptResource
        {
            Name = "NewScript",
            Description = "Desc",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.JavaScript,
            Code = "console.log('test');",
            InputsJson = "{}",
            IsEnabled = true,
            TargetCategories = new List<string> { "tv" },
            TargetTagIds = new List<int> { 5 },
        };

        this.automationService.Add(Arg.Any<AutomationScript>()).Returns(callInfo =>
        {
            var passed = callInfo.Arg<AutomationScript>();
            passed.Id = 123;
            return passed;
        });

        var result = this.controller.Create(resource);

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(123);
        result.Value.Name.Should().Be("NewScript");
        result.Value.TargetCategories.Should().Contain("tv");
        result.Value.TargetTagIds.Should().Contain(5);
        this.automationService.Received(1).Add(Arg.Is<AutomationScript>(s => s.Name == "NewScript"));
    }

    [Test]
    public void Update_WhenResourceIsNull_ReturnsBadRequest()
    {
        var result = this.controller.Update(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Update_WhenNameIsMissingOrWhitespace_ReturnsBadRequest(string name)
    {
        var resource = new AutomationScriptResource { Id = 1, Name = name };

        var result = this.controller.Update(resource);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Update_WhenValid_UpdatesScriptAndReturnsResource()
    {
        var resource = new AutomationScriptResource
        {
            Id = 55,
            Name = "UpdatedScript",
            Trigger = AutomationTrigger.TorrentCompleted,
            Language = AutomationLanguage.Yaml,
        };

        this.automationService.Update(Arg.Any<AutomationScript>()).Returns(callInfo => callInfo.Arg<AutomationScript>());

        var result = this.controller.Update(resource);

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(55);
        result.Value.Name.Should().Be("UpdatedScript");
        this.automationService.Received(1).Update(Arg.Is<AutomationScript>(s => s.Id == 55 && s.Name == "UpdatedScript"));
    }

    [Test]
    public void Delete_CallsAutomationServiceDeleteAndReturnsOk()
    {
        var result = this.controller.Delete(42);

        result.Should().BeOfType<OkResult>();
        this.automationService.Received(1).Delete(42);
    }

    [Test]
    public void Run_ExecutesScriptAndBroadcastsSignalRMessage()
    {
        var execResult = new AutomationExecutionResult
        {
            Success = true,
            ExecutionTimeMs = 45,
            OutputLog = "Ran successfully",
        };
        this.automationService.ExecuteScript(10, 50).Returns(execResult);

        var result = this.controller.Run(10, 50);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        okResult.Value.Should().Be(execResult);

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "AutomationExecuted"));
    }

    [Test]
    public void Test_WhenRequestOrScriptIsNull_ReturnsBadRequest()
    {
        var nullRequestResult = this.controller.Test(null!);
        nullRequestResult.Result.Should().BeOfType<BadRequestObjectResult>();

        var emptyScriptResult = this.controller.Test(new AutomationTestRequestResource { Script = null! });
        emptyScriptResult.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void Test_WhenValid_TestsScriptAndBroadcastsSignalRMessage()
    {
        var testRequest = new AutomationTestRequestResource
        {
            TorrentId = 99,
            Script = new AutomationScriptResource
            {
                Id = 7,
                Name = "TestTrigger",
                Trigger = AutomationTrigger.TorrentAdded,
                Language = AutomationLanguage.JavaScript,
            },
            CustomInputs = new Dictionary<string, object> { { "key", "value" } },
        };

        var execResult = new AutomationExecutionResult
        {
            Success = true,
            ExecutionTimeMs = 12,
            OutputLog = "Test executed",
        };
        this.automationService.TestScript(Arg.Any<AutomationScript>(), 99, testRequest.CustomInputs).Returns(execResult);

        var result = this.controller.Test(testRequest);

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        okResult.Value.Should().Be(execResult);

        this.signalRBroadcaster.Received(1).BroadcastMessage(Arg.Is<SignalRMessage>(m =>
            m.Name == "AutomationTriggerEvaluated"));
    }

    [Test]
    public void GetMarketplaceTemplates_ReturnsTemplatesFromMarketplaceService()
    {
        var templates = new List<AutomationMarketplaceTemplate>
        {
            new() { Id = "template-1", Name = "Template 1" },
            new() { Id = "template-2", Name = "Template 2" },
        };
        this.marketplaceService.GetTemplates().Returns(templates);

        var result = this.controller.GetMarketplaceTemplates();

        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        okResult.Value.Should().Be(templates);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void InstallMarketplaceTemplate_WhenTemplateIdIsMissing_ReturnsBadRequest(string templateId)
    {
        var request = new InstallMarketplaceTemplateRequest { TemplateId = templateId };

        var result = this.controller.InstallMarketplaceTemplate(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().Be("Template ID is required.");
    }

    [Test]
    public void InstallMarketplaceTemplate_WhenRequestIsNull_ReturnsBadRequest()
    {
        var result = this.controller.InstallMarketplaceTemplate(null!);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Test]
    public void InstallMarketplaceTemplate_WhenMarketplaceServiceThrows_ReturnsBadRequestWithMessage()
    {
        var request = new InstallMarketplaceTemplateRequest { TemplateId = "invalid-template" };
        this.marketplaceService.InstallTemplate("invalid-template", null, null)
            .Throws(new InvalidOperationException("Template with ID 'invalid-template' was not found."));

        var result = this.controller.InstallMarketplaceTemplate(request);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = (BadRequestObjectResult)result.Result!;
        badRequest.Value.Should().Be("Template with ID 'invalid-template' was not found.");
    }

    [Test]
    public void InstallMarketplaceTemplate_WhenSuccessful_InstallsAndAddsScript()
    {
        var request = new InstallMarketplaceTemplateRequest
        {
            TemplateId = "pt-auto-zap-tokens",
            CustomName = "Installed Zap",
            CustomInputs = new Dictionary<string, string> { { "domain", "test.org" } },
        };

        var createdScript = new AutomationScript
        {
            Name = "Installed Zap",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.JavaScript,
        };
        var addedScript = new AutomationScript
        {
            Id = 77,
            Name = "Installed Zap",
            Trigger = AutomationTrigger.TorrentAdded,
            Language = AutomationLanguage.JavaScript,
        };

        this.marketplaceService.InstallTemplate("pt-auto-zap-tokens", "Installed Zap", request.CustomInputs).Returns(createdScript);
        this.automationService.Add(createdScript).Returns(addedScript);

        var result = this.controller.InstallMarketplaceTemplate(request);

        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be(77);
        result.Value.Name.Should().Be("Installed Zap");
    }
}
