// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.Backup;
using Leecharr.Api.V1.Categories;
using Leecharr.Api.V1.Config;
using Leecharr.Api.V1.DownloadClients;
using Leecharr.Api.V1.FileBrowser;
using Leecharr.Api.V1.Indexers;
using Leecharr.Api.V1.Notifications;
using Leecharr.Api.V1.System;
using Leecharr.Api.V1.Torrents;
using Microsoft.AspNetCore.Authorization;
using NUnit.Framework;

namespace Leecharr.Core.Test.Security;

[TestFixture]
public class ControllerAuthorizationPolicyTest
{
    [TestCase(typeof(GeneralConfigController), "RequireAdmin")]
    [TestCase(typeof(BitTorrentConfigController), "RequireAdmin")]
    [TestCase(typeof(NetworkConfigController), "RequireAdmin")]
    [TestCase(typeof(BackupController), "RequireAdmin")]
    [TestCase(typeof(FileBrowserController), "RequireAdmin")]
    [TestCase(typeof(SystemMaintenanceController), "RequireAdmin")]
    [TestCase(typeof(SystemTaskController), "RequireAdmin")]
    [TestCase(typeof(NotificationController), "RequireAdmin")]
    [TestCase(typeof(IdentityProviderConfigController), "RequireAdmin")]
    public void AdministrativeControllers_HaveRequireAdminPolicy(Type controllerType, string expectedPolicy)
    {
        var authAttr = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        authAttr.Should().NotBeNull($"Controller {controllerType.Name} must have an [Authorize] attribute");
        authAttr!.Policy.Should().Be(expectedPolicy);
    }

    [TestCase(typeof(TorrentController), "RequireOperator")]
    [TestCase(typeof(CategoryController), "RequireOperator")]
    [TestCase(typeof(DownloadClientController), "RequireOperator")]
    [TestCase(typeof(IndexerController), "RequireOperator")]
    [TestCase(typeof(ArrConnectionController), "RequireOperator")]
    public void OperatorControllers_HaveRequireOperatorPolicy(Type controllerType, string expectedPolicy)
    {
        var authAttr = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .FirstOrDefault();

        authAttr.Should().NotBeNull($"Controller {controllerType.Name} must have an [Authorize] attribute");
        authAttr!.Policy.Should().Be(expectedPolicy);
    }
}
