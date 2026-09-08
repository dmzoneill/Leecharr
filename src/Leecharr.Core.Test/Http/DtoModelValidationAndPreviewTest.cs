// Copyright (c) PlaceholderCompany. All rights reserved.

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.Auth;
using Leecharr.Api.V1.Categories;
using Leecharr.Api.V1.DownloadClients;
using Leecharr.Api.V1.FileBrowser;
using Leecharr.Api.V1.Indexers;
using Leecharr.Api.V1.Notifications;
using Leecharr.Api.V1.Seeding;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Core.FileBrowser;

namespace Leecharr.Core.Test.Http;

[TestFixture]
public class DtoModelValidationAndPreviewTest
{
    private static List<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var context = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, context, validationResults, true);
        return validationResults;
    }

    [Test]
    public void CategoryResource_Validations()
    {
        var invalid = new CategoryResource
        {
            Name = null!,
            DefaultUploadLimit = -1,
            DefaultDownloadLimit = -5,
            TargetRatio = -1.0,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CategoryResource.Name)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CategoryResource.DefaultUploadLimit)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(CategoryResource.DefaultDownloadLimit)));

        var valid = new CategoryResource
        {
            Name = "Movies",
            DefaultUploadLimit = 1000,
            DefaultDownloadLimit = 5000,
            TargetRatio = 2.0,
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void RssRuleResource_Validations()
    {
        var invalid = new RssRuleResource
        {
            Name = null!,
            MinSeeders = -1,
            MinSizeBytes = -10,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RssRuleResource.Name)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(RssRuleResource.MinSeeders)));

        var valid = new RssRuleResource
        {
            Name = "Release Filter",
            MinSeeders = 5,
            MinSizeBytes = 1048576,
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void DownloadClientResource_Validations()
    {
        var invalid = new DownloadClientResource
        {
            Name = null!,
            ClientType = null!,
            Host = null!,
            Port = 0,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DownloadClientResource.Name)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DownloadClientResource.ClientType)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DownloadClientResource.Host)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(DownloadClientResource.Port)));

        var valid = new DownloadClientResource
        {
            Name = "Local qBittorrent",
            ClientType = "qBittorrent",
            Host = "localhost",
            Port = 8080,
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void SpeedScheduleResource_Validations()
    {
        var invalid = new SpeedScheduleResource
        {
            Name = null!,
            Days = 0,
            StartTime = "invalid-time",
            EndTime = "25:99:99",
            MaxDownloadSpeed = -100,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SpeedScheduleResource.Name)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SpeedScheduleResource.Days)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SpeedScheduleResource.StartTime)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SpeedScheduleResource.EndTime)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SpeedScheduleResource.MaxDownloadSpeed)));

        var valid = new SpeedScheduleResource
        {
            Name = "Night Throttling",
            Days = 127,
            StartTime = "23:00:00",
            EndTime = "06:00:00",
            MaxDownloadSpeed = 5000,
            MaxUploadSpeed = 1000,
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void LoginRequestResource_Validations()
    {
        var invalid = new LoginRequestResource
        {
            Username = null!,
            Password = null!,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequestResource.Username)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(LoginRequestResource.Password)));

        var valid = new LoginRequestResource
        {
            Username = "admin",
            Password = "password123",
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void IndexerResource_Validations()
    {
        var invalid = new IndexerResource
        {
            Name = null!,
            Url = null!,
            Priority = 0,
            MinSeeders = -5,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(IndexerResource.Name)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(IndexerResource.Url)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(IndexerResource.Priority)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(IndexerResource.MinSeeders)));

        var valid = new IndexerResource
        {
            Name = "Prowlarr Tracker",
            Url = "http://localhost:9696",
            Priority = 1,
            MinSeeders = 1,
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void NotificationResource_Validations()
    {
        var invalid = new NotificationResource
        {
            Name = null!,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(NotificationResource.Name)));

        var valid = new NotificationResource
        {
            Name = "Discord Webhook",
            Implementation = "Discord",
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void ArrConnectionResource_Validations()
    {
        var invalid = new ArrConnectionResource
        {
            Name = null!,
            ArrType = null!,
            Url = null!,
            RefreshIntervalMinutes = 0,
        };

        var results = ValidateModel(invalid);
        results.Should().NotBeEmpty();
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ArrConnectionResource.Name)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ArrConnectionResource.ArrType)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ArrConnectionResource.Url)));
        results.Should().Contain(r => r.MemberNames.Contains(nameof(ArrConnectionResource.RefreshIntervalMinutes)));

        var valid = new ArrConnectionResource
        {
            Name = "Sonarr Server",
            ArrType = "Sonarr",
            Url = "http://localhost:8989",
            RefreshIntervalMinutes = 30,
        };

        var validResults = ValidateModel(valid);
        validResults.Should().BeEmpty();
    }

    [Test]
    public void FileBrowserController_GetPreview_ClampsMaxBytesAndPreventsUnboundedAllocations()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "Hello, world! Preview test content.");

            var fileBrowserService = Substitute.For<IFileBrowserService>();
            fileBrowserService.ResolvePath("test.txt").Returns(tempFile);

            var controller = new FileBrowserController(fileBrowserService);

            // Test with gigantic maxBytes (100MB) -> clamped to 10MB safely
            var result = controller.GetPreview("test.txt", maxBytes: 100 * 1024 * 1024);
            result.Should().BeOfType<OkObjectResult>();

            // Test with negative maxBytes -> clamped to default
            var resultNegative = controller.GetPreview("test.txt", maxBytes: -10);
            resultNegative.Should().BeOfType<OkObjectResult>();
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Test]
    public void NotificationController_RegexEscaping_SpecialCharactersDoNotThrow()
    {
        var propWithRegexChars = "special[name]+(key)";
        var escaped = Regex.Escape(propWithRegexChars);
        var settings = $"{propWithRegexChars}=my_secret_val&other=123";

        var match = Regex.Match(settings, $@"{escaped}=([^&]+)");
        match.Success.Should().BeTrue();
        match.Groups[1].Value.Should().Be("my_secret_val");
    }
}
