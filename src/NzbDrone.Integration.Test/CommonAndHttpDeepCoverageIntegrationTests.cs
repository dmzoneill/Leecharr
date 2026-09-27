// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using DryIoc;
using FluentAssertions;
using Leecharr.Api.V1.System;
using Leecharr.Http.Authentication;
using Leecharr.Http.REST;
using Leecharr.Http.REST.Attributes;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Integration.Test;

[TestFixture]
public class CommonAndHttpDeepCoverageIntegrationTests : IntegrationTestBase
{
    public interface IDeepTestService
    {
        string GetMessage();
    }

    public interface IDeepSecondTestService
    {
        int GetNumber();
    }

    public class DeepTestService : IDeepTestService, IDeepSecondTestService
    {
        public string GetMessage() => "DeepTest";

        public int GetNumber() => 42;
    }

    public class DeepDisposableService : IDeepTestService, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public string GetMessage() => "Disposable";

        public void Dispose()
        {
            this.IsDisposed = true;
        }
    }

    public class DeepConsumerService
    {
        public IDeepTestService Service { get; }

        public DeepConsumerService(IDeepTestService service)
        {
            this.Service = service;
        }
    }

    public class DeepRestItemResource : RestResource
    {
        public string Title { get; set; } = string.Empty;
    }

    public class DeepRestDevice : RestResource
    {
        public string HardwareId { get; set; } = string.Empty;
    }

    public class DeepCustomOverriddenResource : RestResource
    {
        public override string ResourceName => "custom-resource-name";
    }

    public class DeepSampleRestController
    {
        [RestPutById]
        public void UpdateResource(int? id)
        {
        }
    }

    [TearDown]
    public void Cleanup()
    {
        ApiKeyAuthenticationHandler.ResetThrottling();
    }

    // ------------------------------------------------------------------------
    // Part 1: Leecharr.Common - Container and ContainerExtensions
    // ------------------------------------------------------------------------

    [Test]
    public void ContainerBuilder_Build_ConfiguresRulesAndResolvesConcreteTypes()
    {
        using var container = ContainerBuilder.Build();
        container.Should().NotBeNull();

        // AutoConcreteTypeResolution allows resolving concrete class without explicit registration
        var resolved = container.Resolve<DeepTestService>();
        resolved.Should().NotBeNull();
        resolved.GetMessage().Should().Be("DeepTest");

        // By default WithNzbDroneRules applies Reuse.Singleton
        var resolvedAgain = container.Resolve<DeepTestService>();
        resolvedAgain.Should().BeSameAs(resolved);
    }

    [Test]
    public void Container_RegisterInstance_ResolvesExactInstance()
    {
        using var container = ContainerBuilder.Build();
        var customInstance = new DeepTestService();

        container.Use<IDeepTestService>(customInstance);

        var resolved = container.Resolve<IDeepTestService>();
        resolved.Should().BeSameAs(customInstance);
        resolved.GetMessage().Should().Be("DeepTest");
    }

    [Test]
    public void Container_SingletonResolution_ReturnsSameInstanceAcrossResolutions()
    {
        using var container = ContainerBuilder.Build();
        container.Register<DeepTestService>(Reuse.Singleton);

        var instance1 = container.Resolve<DeepTestService>();
        var instance2 = container.Resolve<DeepTestService>();

        instance1.Should().BeSameAs(instance2);
    }

    [Test]
    public void Container_FactoryResolution_InvokesFactoryDelegate()
    {
        using var container = ContainerBuilder.Build();
        var creationCount = 0;

        container.RegisterDelegate<IDeepTestService>(_ =>
        {
            creationCount++;
            return new DeepTestService();
        }, Reuse.Transient);

        var instance1 = container.Resolve<IDeepTestService>();
        var instance2 = container.Resolve<IDeepTestService>();

        instance1.Should().NotBeNull();
        instance2.Should().NotBeNull();
        instance1.Should().NotBeSameAs(instance2);
        creationCount.Should().Be(2);

        // Factory resolution via Func<T>
        var factory = container.Resolve<Func<IDeepTestService>>();
        var factoryCreated = factory();
        factoryCreated.Should().NotBeNull();
        creationCount.Should().Be(3);
    }

    [Test]
    public void Container_LifetimeScopes_ResolvesScopedInstancesAndDisposesCleanly()
    {
        using var container = ContainerBuilder.Build();
        container.Register<DeepDisposableService>(Reuse.Scoped);

        DeepDisposableService scopedInstance;
        using (var scope = container.OpenScope())
        {
            scopedInstance = scope.Resolve<DeepDisposableService>();
            scopedInstance.Should().NotBeNull();
            scopedInstance.IsDisposed.Should().BeFalse();

            var scopedAgain = scope.Resolve<DeepDisposableService>();
            scopedAgain.Should().BeSameAs(scopedInstance);
        }

        scopedInstance.IsDisposed.Should().BeTrue();
    }

    [Test]
    public void Container_RegisterSingletonWithInterfaces_Generic_MapsAllInterfaces()
    {
        using var container = ContainerBuilder.Build();
        container.RegisterSingletonWithInterfaces<DeepTestService>();

        var concrete = container.Resolve<DeepTestService>();
        var iface1 = container.Resolve<IDeepTestService>();
        var iface2 = container.Resolve<IDeepSecondTestService>();

        concrete.Should().NotBeNull();
        iface1.Should().BeSameAs(concrete);
        iface2.Should().BeSameAs(concrete);
        iface1.GetMessage().Should().Be("DeepTest");
        iface2.GetNumber().Should().Be(42);
    }

    [Test]
    public void Container_RegisterSingletonWithInterfaces_Type_MapsAllInterfaces()
    {
        using var container = ContainerBuilder.Build();
        container.RegisterSingletonWithInterfaces(typeof(DeepTestService));

        var concrete = container.Resolve<DeepTestService>();
        var iface1 = container.Resolve<IDeepTestService>();
        var iface2 = container.Resolve<IDeepSecondTestService>();

        concrete.Should().NotBeNull();
        iface1.Should().BeSameAs(concrete);
        iface2.Should().BeSameAs(concrete);
    }

    [Test]
    public void Container_RegisterSingleton_MapsInterfaceToImplementation()
    {
        using var container = ContainerBuilder.Build();
        container.RegisterSingleton<IDeepTestService, DeepTestService>();

        var resolved = container.Resolve<IDeepTestService>();
        resolved.Should().NotBeNull();
        resolved.Should().BeOfType<DeepTestService>();
        resolved.GetMessage().Should().Be("DeepTest");

        var resolvedAgain = container.Resolve<IDeepTestService>();
        resolvedAgain.Should().BeSameAs(resolved);
    }

    [Test]
    public void Container_DisposingContainer_DisposesSingletonsAndPreventsResolution()
    {
        DeepDisposableService singleton;
        var container = ContainerBuilder.Build();
        container.Register<DeepDisposableService>(Reuse.Singleton);

        singleton = container.Resolve<DeepDisposableService>();
        singleton.IsDisposed.Should().BeFalse();

        container.Dispose();
        singleton.IsDisposed.Should().BeTrue();

        Action act = () => container.Resolve<DeepDisposableService>();
        act.Should().Throw<ContainerException>();
    }

    [Test]
    public void Container_ConstructorDependencyResolution_ResolvesRegisteredDependencies()
    {
        using var container = ContainerBuilder.Build();
        container.RegisterSingleton<IDeepTestService, DeepTestService>();
        container.Register<DeepConsumerService>(Reuse.Singleton);

        var consumer = container.Resolve<DeepConsumerService>();
        consumer.Should().NotBeNull();
        consumer.Service.Should().NotBeNull();
        consumer.Service.GetMessage().Should().Be("DeepTest");
    }

    [Test]
    public void ContainerBuilder_BuildWithAssemblyNames_AutoAddsServices()
    {
        using var container = ContainerBuilder.Build(new List<string> { "Leecharr.Common" });
        container.Should().NotBeNull();

        // Validate that known types were registered
        KnownTypes.GetImplementations(typeof(object)).Should().NotBeEmpty();
    }

    // ------------------------------------------------------------------------
    // Part 1: Leecharr.Common - AssemblyLoader
    // ------------------------------------------------------------------------

    [Test]
    public void AssemblyLoader_Load_LoadsValidAssemblies()
    {
        var assemblies = AssemblyLoader.Load(new List<string> { "Leecharr.Common", "Leecharr.Http" });

        assemblies.Should().NotBeNull();
        assemblies.Should().HaveCount(2);

        var names = assemblies.Select(a => a.GetName().Name).ToList();
        names.Should().Contain("Leecharr.Common");
        names.Should().Contain("Leecharr.Http");
    }

    [Test]
    public void AssemblyLoader_Load_WithNonExistentAssembly_GracefullyHandlesWithoutThrowing()
    {
        var assemblies = AssemblyLoader.Load(new List<string> { "NonExistentAssembly_XyZ123999" });

        assemblies.Should().NotBeNull();
        assemblies.Should().BeEmpty();
    }

    [Test]
    public void AssemblyLoader_Load_WithEmptyList_ReturnsEmpty()
    {
        var assemblies = AssemblyLoader.Load(new List<string>());

        assemblies.Should().NotBeNull();
        assemblies.Should().BeEmpty();
    }

    [Test]
    public void AssemblyLoader_Load_WithMixedValidAndInvalidAssemblies_ReturnsOnlyValid()
    {
        var assemblies = AssemblyLoader.Load(new List<string> { "Leecharr.Common", "FakeAssembly_998877" });

        assemblies.Should().NotBeNull();
        assemblies.Should().HaveCount(1);
        assemblies[0].GetName().Name.Should().Be("Leecharr.Common");
    }

    // ------------------------------------------------------------------------
    // Part 1: Leecharr.Common - BuildInfo
    // ------------------------------------------------------------------------

    [Test]
    public void BuildInfo_InspectProperties_ReturnsValidMetadata()
    {
        BuildInfo.Version.Should().NotBeNull();
        BuildInfo.Version.Major.Should().BeGreaterThanOrEqualTo(0);
        BuildInfo.Version.ToString().Should().NotBeNullOrWhiteSpace();

        BuildInfo.AppName.Should().Be("Leecharr");
        BuildInfo.Branch.Should().Be("main");
    }

    [Test]
    public void BuildInfo_ReadVersionFile_ViaReflection_ReturnsParsedVersion()
    {
        var method = typeof(BuildInfo).GetMethod("ReadVersionFile", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, null);
        result.Should().NotBeNull();
        result.Should().BeOfType<Version>();

        var parsedVersion = (Version)result!;
        parsedVersion.Major.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void SystemStatusResource_InspectProperties_BuildDateAndIsProductionFlags()
    {
        var status = new SystemStatusResource
        {
            InstanceUuid = "test-uuid-001",
            OsName = "Linux",
            OsVersion = "6.6.0",
            RuntimeVersion = "10.0.0",
            IsLinux = true,
            IsDocker = false,
            IsWindows = false,
            IsOsx = false,
            AppDataFolder = "/config",
            StartupPath = "/app",
        };

        status.AppName.Should().Be("Leecharr");
        status.Version.Should().Be(BuildInfo.Version.ToString());
        status.Branch.Should().Be("main");
        status.RuntimeName.Should().Be(".NET");
        status.IsProduction.Should().Be(!status.IsDebug);
        status.StartTime.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
        status.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
        status.AppDataPath.Should().Be("/config");
        status.StartupPath.Should().Be("/app");
        status.InstanceUuid.Should().Be("test-uuid-001");
        status.OsName.Should().Be("Linux");
    }

    // ------------------------------------------------------------------------
    // Part 2: Leecharr.Http - HostHeaderValidationMiddleware
    // ------------------------------------------------------------------------

    [Test]
    public void HostHeaderValidation_IsHostAllowed_LoopbackVariations()
    {
        // Standard loopback hostnames
        HostHeaderValidationMiddleware.IsHostAllowed("localhost", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("LOCALHOST", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("127.0.0.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("::1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("[::1]", null).Should().BeTrue();

        // Whitespace-padded loopbacks
        HostHeaderValidationMiddleware.IsHostAllowed("  localhost  ", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("  127.0.0.1  ", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("  [::1]  ", null).Should().BeTrue();

        // IPv4 loopback subnet variations
        HostHeaderValidationMiddleware.IsHostAllowed("127.0.0.2", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("127.100.200.1", null).Should().BeTrue();

        // IPv4-mapped IPv6 loopbacks
        HostHeaderValidationMiddleware.IsHostAllowed("::ffff:127.0.0.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("[::ffff:127.0.0.1]", null).Should().BeTrue();
    }

    [Test]
    public void HostHeaderValidation_IsHostAllowed_IPv6BracketedHosts()
    {
        // Link-local bracketed IPv6
        HostHeaderValidationMiddleware.IsHostAllowed("[fe80::1]", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("[fe80::200:5aee:feaa:20a2]", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("fe80::1", null).Should().BeTrue();

        // Site-local bracketed IPv6
        HostHeaderValidationMiddleware.IsHostAllowed("[fec0::1]", null).Should().BeTrue();

        // Unique Local Address (fc00::/7) bracketed IPv6
        HostHeaderValidationMiddleware.IsHostAllowed("[fc00::1]", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("[fd12:3456:789a:1::1]", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("fd00::1", null).Should().BeTrue();

        // Public IPv6 without config is rejected
        HostHeaderValidationMiddleware.IsHostAllowed("[2001:db8::1]", null).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("2001:db8::1", null).Should().BeFalse();

        // Public IPv6 with explicit config is allowed
        HostHeaderValidationMiddleware.IsHostAllowed("[2001:db8::1]", "2001:db8::1").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("[2001:db8::1]", "[2001:db8::1]").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("2001:db8::1", "2001:db8::1").Should().BeTrue();
    }

    [Test]
    public void HostHeaderValidation_IsHostAllowed_PrivateIPv4Ranges()
    {
        // 10.0.0.0/8
        HostHeaderValidationMiddleware.IsHostAllowed("10.0.0.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("10.254.1.99", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("10.255.255.254", null).Should().BeTrue();

        // 172.16.0.0/12
        HostHeaderValidationMiddleware.IsHostAllowed("172.16.0.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("172.24.12.34", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("172.31.255.254", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("172.15.255.254", null).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("172.32.0.1", null).Should().BeFalse();

        // 192.168.0.0/16
        HostHeaderValidationMiddleware.IsHostAllowed("192.168.0.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("192.168.1.100", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("192.168.254.254", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("192.169.1.1", null).Should().BeFalse();

        // 169.254.0.0/16 Link-Local
        HostHeaderValidationMiddleware.IsHostAllowed("169.254.1.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("169.254.200.200", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("169.255.1.1", null).Should().BeFalse();

        // 100.64.0.0/10 CGNAT / Tailscale
        HostHeaderValidationMiddleware.IsHostAllowed("100.64.0.1", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("100.100.50.25", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("100.127.255.254", null).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("100.63.255.254", null).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("100.128.0.1", null).Should().BeFalse();

        // Public IPs rejected without explicit config
        HostHeaderValidationMiddleware.IsHostAllowed("8.8.8.8", null).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("1.1.1.1", null).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("203.0.113.50", null).Should().BeFalse();
    }

    [Test]
    public void HostHeaderValidation_IsHostAllowed_WildcardsAndSubdomains()
    {
        // Asterisk wildcard allows anything
        HostHeaderValidationMiddleware.IsHostAllowed("evil.attacker.com", "*").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("random.domain.net", "*").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("198.51.100.1", "*").Should().BeTrue();

        // Asterisk in list with other entries
        HostHeaderValidationMiddleware.IsHostAllowed("anything.io", "app.local, *").Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("anything.io", "*, app.local").Should().BeTrue();

        // Wildcard prefix *.domain.com
        var wildcardConfig = "*.leecharr.org";
        HostHeaderValidationMiddleware.IsHostAllowed("sub.leecharr.org", wildcardConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("deep.sub.leecharr.org", wildcardConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("a.b.c.leecharr.org", wildcardConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("fakeleecharr.org", wildcardConfig).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("leecharr.org", wildcardConfig).Should().BeFalse();

        // Dot prefix .domain.com
        var dotConfig = ".leecharr.org";
        HostHeaderValidationMiddleware.IsHostAllowed("media.leecharr.org", dotConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("nested.media.leecharr.org", dotConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("fakeleecharr.org", dotConfig).Should().BeFalse();

        // Exact match
        var exactConfig = "media.server.org";
        HostHeaderValidationMiddleware.IsHostAllowed("media.server.org", exactConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("MEDIA.SERVER.ORG", exactConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("other.server.org", exactConfig).Should().BeFalse();
    }

    [Test]
    public void HostHeaderValidation_IsHostAllowed_CommaSemicolonSpaceDelimiters()
    {
        // Comma delimited
        var commaConfig = "alpha.local,beta.local,gamma.local";
        HostHeaderValidationMiddleware.IsHostAllowed("alpha.local", commaConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("beta.local", commaConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("gamma.local", commaConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("delta.local", commaConfig).Should().BeFalse();

        // Semicolon delimited
        var semiConfig = "one.internal;two.internal;three.internal";
        HostHeaderValidationMiddleware.IsHostAllowed("one.internal", semiConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("two.internal", semiConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("three.internal", semiConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("four.internal", semiConfig).Should().BeFalse();

        // Space delimited
        var spaceConfig = "first.net second.net third.net";
        HostHeaderValidationMiddleware.IsHostAllowed("first.net", spaceConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("second.net", spaceConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("third.net", spaceConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("fourth.net", spaceConfig).Should().BeFalse();

        // Mixed delimiters and whitespace
        var mixedConfig = "srv1.local,  srv2.local;  *.wild.net   backup.lan";
        HostHeaderValidationMiddleware.IsHostAllowed("srv1.local", mixedConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("srv2.local", mixedConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("node.wild.net", mixedConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("backup.lan", mixedConfig).Should().BeTrue();
        HostHeaderValidationMiddleware.IsHostAllowed("unknown.lan", mixedConfig).Should().BeFalse();
    }

    [Test]
    public void HostHeaderValidation_IsHostAllowed_NullOrEmptyOrWhitespace_ReturnsFalse()
    {
        HostHeaderValidationMiddleware.IsHostAllowed(null, "example.com").Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed(string.Empty, "example.com").Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed("   ", "example.com").Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed(null, null).Should().BeFalse();
        HostHeaderValidationMiddleware.IsHostAllowed(string.Empty, null).Should().BeFalse();
    }

    [Test]
    public async Task HostHeaderValidationMiddleware_InvokeAsync_ExecutesWithPortPermutations()
    {
        var configService = Substitute.For<IConfigService>();
        configService.HostHeaderValidationEnabled.Returns(true);
        configService.AllowedHosts.Returns("media.app.local, *.cloud.io");

        var nextInvoked = false;
        var middleware = new HostHeaderValidationMiddleware(context =>
        {
            nextInvoked = true;
            context.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        // 1. Valid host with port (e.g. localhost:8080)
        nextInvoked = false;
        var ctx1 = new DefaultHttpContext();
        ctx1.Request.Host = new HostString("localhost", 8080);
        await middleware.InvokeAsync(ctx1, configService);
        nextInvoked.Should().BeTrue();
        ctx1.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 2. Valid IPv4 loopback with port (127.0.0.1:8989)
        nextInvoked = false;
        var ctx2 = new DefaultHttpContext();
        ctx2.Request.Host = new HostString("127.0.0.1", 8989);
        await middleware.InvokeAsync(ctx2, configService);
        nextInvoked.Should().BeTrue();
        ctx2.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 3. Valid IPv6 bracketed loopback with port ([::1]:5000)
        nextInvoked = false;
        var ctx3 = new DefaultHttpContext();
        ctx3.Request.Host = new HostString("[::1]", 5000);
        await middleware.InvokeAsync(ctx3, configService);
        nextInvoked.Should().BeTrue();
        ctx3.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 4. Allowed hostname from config with port (media.app.local:443)
        nextInvoked = false;
        var ctx4 = new DefaultHttpContext();
        ctx4.Request.Host = new HostString("media.app.local", 443);
        await middleware.InvokeAsync(ctx4, configService);
        nextInvoked.Should().BeTrue();
        ctx4.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 5. Allowed wildcard subdomain with port (node1.cloud.io:8443)
        nextInvoked = false;
        var ctx5 = new DefaultHttpContext();
        ctx5.Request.Host = new HostString("node1.cloud.io", 8443);
        await middleware.InvokeAsync(ctx5, configService);
        nextInvoked.Should().BeTrue();
        ctx5.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 6. Disallowed host with port (attacker.com:8080) -> 400 Bad Request
        nextInvoked = false;
        var ctx6 = new DefaultHttpContext();
        ctx6.Response.Body = new MemoryStream();
        ctx6.Request.Host = new HostString("attacker.com", 8080);
        await middleware.InvokeAsync(ctx6, configService);
        nextInvoked.Should().BeFalse();
        ctx6.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        ctx6.Response.ContentType.Should().Be("text/plain");

        ctx6.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(ctx6.Response.Body);
        var body = await reader.ReadToEndAsync();
        body.Should().Be("Invalid Host header.");

        // 7. When validation is disabled, disallowed host passes through
        configService.HostHeaderValidationEnabled.Returns(false);
        nextInvoked = false;
        var ctx7 = new DefaultHttpContext();
        ctx7.Request.Host = new HostString("attacker.com", 8080);
        await middleware.InvokeAsync(ctx7, configService);
        nextInvoked.Should().BeTrue();
        ctx7.Response.StatusCode.Should().Be(StatusCodes.Status200OK);

        // 8. When configService is null, passes through
        nextInvoked = false;
        var ctx8 = new DefaultHttpContext();
        ctx8.Request.Host = new HostString("anyhost.com", 9999);
        await middleware.InvokeAsync(ctx8, null);
        nextInvoked.Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // Part 2: Leecharr.Http - ApiKeyAuthenticationHandler
    // ------------------------------------------------------------------------

    private ApiKeyAuthenticationHandler CreateApiKeyHandler(
        HttpContext context,
        IConfigFileProvider configProvider,
        ITrustedNetworkService trustedNetworkService = null,
        IConfigService configService = null,
        string customHeaderName = null)
    {
        var options = new ApiKeyAuthenticationOptions();
        if (!string.IsNullOrEmpty(customHeaderName))
        {
            options.HeaderName = customHeaderName;
        }

        var optionsMonitor = Substitute.For<IOptionsMonitor<ApiKeyAuthenticationOptions>>();
        optionsMonitor.Get(ApiKeyAuthenticationOptions.DefaultScheme).Returns(options);

        var handler = new ApiKeyAuthenticationHandler(
            optionsMonitor,
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            configProvider,
            trustedNetworkService: trustedNetworkService,
            configService: configService);

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationOptions.DefaultScheme,
            null,
            typeof(ApiKeyAuthenticationHandler));

        handler.InitializeAsync(scheme, context).GetAwaiter().GetResult();
        return handler;
    }

    [Test]
    public async Task ApiKeyAuth_QueryParamVariations_AuthenticatesSuccessfully()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.AuthenticationEnabled.Returns(true);
        configProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.Enabled);
        configProvider.ApiKey.Returns("secret-token-xyz");

        // 1. Query parameter ?api_key=
        var ctx1 = new DefaultHttpContext();
        ctx1.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx1.Request.QueryString = new QueryString("?api_key=secret-token-xyz");
        var h1 = this.CreateApiKeyHandler(ctx1, configProvider);
        var r1 = await h1.AuthenticateAsync();
        r1.Succeeded.Should().BeTrue();
        r1.Principal!.Identity!.Name.Should().Be("MasterApiKey");

        // 2. Query parameter ?access_token=
        var ctx2 = new DefaultHttpContext();
        ctx2.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx2.Request.QueryString = new QueryString("?access_token=secret-token-xyz");
        var h2 = this.CreateApiKeyHandler(ctx2, configProvider);
        var r2 = await h2.AuthenticateAsync();
        r2.Succeeded.Should().BeTrue();
        r2.Principal!.Identity!.Name.Should().Be("MasterApiKey");

        // 3. Query parameter ?apikey=
        var ctx3 = new DefaultHttpContext();
        ctx3.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx3.Request.QueryString = new QueryString("?apikey=secret-token-xyz");
        var h3 = this.CreateApiKeyHandler(ctx3, configProvider);
        var r3 = await h3.AuthenticateAsync();
        r3.Succeeded.Should().BeTrue();
        r3.Principal!.Identity!.Name.Should().Be("MasterApiKey");

        // 4. Multiple query parameters with ?page=1&apikey=...&filter=all
        var ctx4 = new DefaultHttpContext();
        ctx4.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx4.Request.QueryString = new QueryString("?page=1&apikey=secret-token-xyz&filter=all");
        var h4 = this.CreateApiKeyHandler(ctx4, configProvider);
        var r4 = await h4.AuthenticateAsync();
        r4.Succeeded.Should().BeTrue();

        // 5. Empty query parameters return NoResult
        var ctx5 = new DefaultHttpContext();
        ctx5.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx5.Request.QueryString = new QueryString("?api_key=");
        var h5 = this.CreateApiKeyHandler(ctx5, configProvider);
        var r5 = await h5.AuthenticateAsync();
        r5.None.Should().BeTrue();

        var ctx6 = new DefaultHttpContext();
        ctx6.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx6.Request.QueryString = new QueryString("?access_token=");
        var h6 = this.CreateApiKeyHandler(ctx6, configProvider);
        var r6 = await h6.AuthenticateAsync();
        r6.None.Should().BeTrue();

        var ctx7 = new DefaultHttpContext();
        ctx7.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx7.Request.QueryString = new QueryString("?apikey=");
        var h7 = this.CreateApiKeyHandler(ctx7, configProvider);
        var r7 = await h7.AuthenticateAsync();
        r7.None.Should().BeTrue();
    }

    [Test]
    public async Task ApiKeyAuth_HeaderVariations_AuthenticatesSuccessfully()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.AuthenticationEnabled.Returns(true);
        configProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.Enabled);
        configProvider.ApiKey.Returns("auth-key-val");

        // 1. Default header X-Api-Key
        var ctx1 = new DefaultHttpContext();
        ctx1.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx1.Request.Headers["X-Api-Key"] = "auth-key-val";
        var h1 = this.CreateApiKeyHandler(ctx1, configProvider);
        var r1 = await h1.AuthenticateAsync();
        r1.Succeeded.Should().BeTrue();

        // 2. Secondary header ApiKey
        var ctx2 = new DefaultHttpContext();
        ctx2.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx2.Request.Headers["ApiKey"] = "auth-key-val";
        var h2 = this.CreateApiKeyHandler(ctx2, configProvider);
        var r2 = await h2.AuthenticateAsync();
        r2.Succeeded.Should().BeTrue();

        // 3. Authorization: Bearer <key>
        var ctx3 = new DefaultHttpContext();
        ctx3.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx3.Request.Headers["Authorization"] = "Bearer auth-key-val";
        var h3 = this.CreateApiKeyHandler(ctx3, configProvider);
        var r3 = await h3.AuthenticateAsync();
        r3.Succeeded.Should().BeTrue();

        // 4. Authorization: bearer <key> (case-insensitive prefix)
        var ctx4 = new DefaultHttpContext();
        ctx4.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx4.Request.Headers["Authorization"] = "bearer auth-key-val";
        var h4 = this.CreateApiKeyHandler(ctx4, configProvider);
        var r4 = await h4.AuthenticateAsync();
        r4.Succeeded.Should().BeTrue();

        // 5. Authorization: Bearer  <key>  with whitespace
        var ctx5 = new DefaultHttpContext();
        ctx5.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx5.Request.Headers["Authorization"] = "Bearer   auth-key-val  ";
        var h5 = this.CreateApiKeyHandler(ctx5, configProvider);
        var r5 = await h5.AuthenticateAsync();
        r5.Succeeded.Should().BeTrue();

        // 6. Custom header name via options
        var ctx6 = new DefaultHttpContext();
        ctx6.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx6.Request.Headers["X-Custom-Secret"] = "auth-key-val";
        var h6 = this.CreateApiKeyHandler(ctx6, configProvider, customHeaderName: "X-Custom-Secret");
        var r6 = await h6.AuthenticateAsync();
        r6.Succeeded.Should().BeTrue();

        // 7. Non-Bearer authorization header yields NoResult
        var ctx7 = new DefaultHttpContext();
        ctx7.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.1");
        ctx7.Request.Headers["Authorization"] = "Basic dXNlcjpwYXNz";
        var h7 = this.CreateApiKeyHandler(ctx7, configProvider);
        var r7 = await h7.AuthenticateAsync();
        r7.None.Should().BeTrue();
    }

    [Test]
    public async Task ApiKeyAuth_BypassedConfigurations_WithRemoteIpScenarios()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        var trustedService = new TrustedNetworkService();

        // 1. Authentication completely disabled -> always bypassed regardless of remote IP
        configProvider.AuthenticationEnabled.Returns(false);
        var ctx1 = new DefaultHttpContext();
        ctx1.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.195");
        var h1 = this.CreateApiKeyHandler(ctx1, configProvider, trustedNetworkService: trustedService);
        var r1 = await h1.AuthenticateAsync();
        r1.Succeeded.Should().BeTrue();
        r1.Principal!.Identity!.Name.Should().Be("Admin");
        r1.Principal.IsInRole("Admin").Should().BeTrue();
        r1.Principal.IsInRole("Operator").Should().BeTrue();
        r1.Principal.IsInRole("User").Should().BeTrue();

        // Null remote IP when auth is disabled is still bypassed
        var ctxNull = new DefaultHttpContext();
        ctxNull.Connection.RemoteIpAddress = null;
        var hNull = this.CreateApiKeyHandler(ctxNull, configProvider, trustedNetworkService: trustedService);
        var rNull = await hNull.AuthenticateAsync();
        rNull.Succeeded.Should().BeTrue();

        // 2. AuthenticationEnabled = true, AuthenticationRequired = DisabledForLocalhost
        configProvider.AuthenticationEnabled.Returns(true);
        configProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.DisabledForLocalhost);
        configProvider.ApiKey.Returns("secret-key");

        // Loopback IPv4 bypassed
        var ctxLoop4 = new DefaultHttpContext();
        ctxLoop4.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var hLoop4 = this.CreateApiKeyHandler(ctxLoop4, configProvider, trustedNetworkService: trustedService);
        var rLoop4 = await hLoop4.AuthenticateAsync();
        rLoop4.Succeeded.Should().BeTrue();
        rLoop4.Principal!.Identity!.Name.Should().Be("Admin");

        // Loopback IPv6 bypassed
        var ctxLoop6 = new DefaultHttpContext();
        ctxLoop6.Connection.RemoteIpAddress = IPAddress.Parse("::1");
        var hLoop6 = this.CreateApiKeyHandler(ctxLoop6, configProvider, trustedNetworkService: trustedService);
        var rLoop6 = await hLoop6.AuthenticateAsync();
        rLoop6.Succeeded.Should().BeTrue();

        // Loopback IPv4-mapped IPv6 bypassed
        var ctxLoopMapped = new DefaultHttpContext();
        ctxLoopMapped.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
        var hLoopMapped = this.CreateApiKeyHandler(ctxLoopMapped, configProvider, trustedNetworkService: trustedService);
        var rLoopMapped = await hLoopMapped.AuthenticateAsync();
        rLoopMapped.Succeeded.Should().BeTrue();

        // LAN address NOT bypassed when DisabledForLocalhost is set
        var ctxLan = new DefaultHttpContext();
        ctxLan.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.100");
        var hLan = this.CreateApiKeyHandler(ctxLan, configProvider, trustedNetworkService: trustedService);
        var rLan = await hLan.AuthenticateAsync();
        rLan.None.Should().BeTrue();

        // 3. AuthenticationEnabled = true, AuthenticationRequired = DisabledForLocalAddresses
        configProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.DisabledForLocalAddresses);

        // Private 10.x bypassed
        var ctx10 = new DefaultHttpContext();
        ctx10.Connection.RemoteIpAddress = IPAddress.Parse("10.5.10.15");
        var h10 = this.CreateApiKeyHandler(ctx10, configProvider, trustedNetworkService: trustedService);
        var r10 = await h10.AuthenticateAsync();
        r10.Succeeded.Should().BeTrue();

        // Private 172.16.x bypassed
        var ctx172 = new DefaultHttpContext();
        ctx172.Connection.RemoteIpAddress = IPAddress.Parse("172.20.0.1");
        var h172 = this.CreateApiKeyHandler(ctx172, configProvider, trustedNetworkService: trustedService);
        var r172 = await h172.AuthenticateAsync();
        r172.Succeeded.Should().BeTrue();

        // Private 192.168.x bypassed
        var ctx192 = new DefaultHttpContext();
        ctx192.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        var h192 = this.CreateApiKeyHandler(ctx192, configProvider, trustedNetworkService: trustedService);
        var r192 = await h192.AuthenticateAsync();
        r192.Succeeded.Should().BeTrue();

        // Link-local 169.254.x bypassed
        var ctxLinkLocal = new DefaultHttpContext();
        ctxLinkLocal.Connection.RemoteIpAddress = IPAddress.Parse("169.254.10.20");
        var hLinkLocal = this.CreateApiKeyHandler(ctxLinkLocal, configProvider, trustedNetworkService: trustedService);
        var rLinkLocal = await hLinkLocal.AuthenticateAsync();
        rLinkLocal.Succeeded.Should().BeTrue();

        // IPv6 Link-Local fe80:: bypassed
        var ctxFe80 = new DefaultHttpContext();
        ctxFe80.Connection.RemoteIpAddress = IPAddress.Parse("fe80::1");
        var hFe80 = this.CreateApiKeyHandler(ctxFe80, configProvider, trustedNetworkService: trustedService);
        var rFe80 = await hFe80.AuthenticateAsync();
        rFe80.Succeeded.Should().BeTrue();

        // IPv6 ULA fd00:: bypassed
        var ctxFd00 = new DefaultHttpContext();
        ctxFd00.Connection.RemoteIpAddress = IPAddress.Parse("fd00::1");
        var hFd00 = this.CreateApiKeyHandler(ctxFd00, configProvider, trustedNetworkService: trustedService);
        var rFd00 = await hFd00.AuthenticateAsync();
        rFd00.Succeeded.Should().BeTrue();

        // Public IP is NOT bypassed
        var ctxPublic = new DefaultHttpContext();
        ctxPublic.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.50");
        var hPublic = this.CreateApiKeyHandler(ctxPublic, configProvider, trustedNetworkService: trustedService);
        var rPublic = await hPublic.AuthenticateAsync();
        rPublic.None.Should().BeTrue();

        // 4. AuthenticationRequired = Enabled (Never bypassed)
        configProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.Enabled);

        var ctxAlwaysReq = new DefaultHttpContext();
        ctxAlwaysReq.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");
        var hAlwaysReq = this.CreateApiKeyHandler(ctxAlwaysReq, configProvider, trustedNetworkService: trustedService);
        var rAlwaysReq = await hAlwaysReq.AuthenticateAsync();
        rAlwaysReq.None.Should().BeTrue();

        // 5. Remote IP is null when auth is enabled -> not bypassed
        var ctxNullRemote = new DefaultHttpContext();
        ctxNullRemote.Connection.RemoteIpAddress = null;
        var hNullRemote = this.CreateApiKeyHandler(ctxNullRemote, configProvider, trustedNetworkService: trustedService);
        var rNullRemote = await hNullRemote.AuthenticateAsync();
        rNullRemote.None.Should().BeTrue();
    }

    [Test]
    public async Task ApiKeyAuth_InvalidKeyAndThrottling_EnforcesRateLimiting()
    {
        var configProvider = Substitute.For<IConfigFileProvider>();
        configProvider.AuthenticationEnabled.Returns(true);
        configProvider.AuthenticationRequired.Returns(AuthenticationRequiredType.Enabled);
        configProvider.ApiKey.Returns("valid-master-key");

        var testIp = IPAddress.Parse("198.51.100.99");

        // 5 consecutive invalid attempts
        for (var i = 0; i < 5; i++)
        {
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = testIp;
            ctx.Request.Headers["X-Api-Key"] = "wrong-key";
            var handler = this.CreateApiKeyHandler(ctx, configProvider);
            var result = await handler.AuthenticateAsync();
            result.Succeeded.Should().BeFalse();
            result.Failure!.Message.Should().Be("Invalid API Key");
        }

        // 6th attempt should be throttled even with the valid key!
        var throttledCtx = new DefaultHttpContext();
        throttledCtx.Connection.RemoteIpAddress = testIp;
        throttledCtx.Request.Headers["X-Api-Key"] = "valid-master-key";
        var throttledHandler = this.CreateApiKeyHandler(throttledCtx, configProvider);
        var throttledResult = await throttledHandler.AuthenticateAsync();
        throttledResult.Succeeded.Should().BeFalse();
        throttledResult.Failure!.Message.Should().Contain("Too many failed authentication attempts");

        // Resetting throttling clears the lock
        ApiKeyAuthenticationHandler.ResetThrottling();

        var recoveredCtx = new DefaultHttpContext();
        recoveredCtx.Connection.RemoteIpAddress = testIp;
        recoveredCtx.Request.Headers["X-Api-Key"] = "valid-master-key";
        var recoveredHandler = this.CreateApiKeyHandler(recoveredCtx, configProvider);
        var recoveredResult = await recoveredHandler.AuthenticateAsync();
        recoveredResult.Succeeded.Should().BeTrue();
    }

    // ------------------------------------------------------------------------
    // Part 2: Leecharr.Http - RestResource and RestPutByIdAttribute
    // ------------------------------------------------------------------------

    [Test]
    public void RestResource_ResourceName_DerivesFromClassName()
    {
        var item = new DeepRestItemResource();
        item.ResourceName.Should().Be("deeprestitem");

        var device = new DeepRestDevice();
        device.ResourceName.Should().Be("deeprestdevice");

        var overridden = new DeepCustomOverriddenResource();
        overridden.ResourceName.Should().Be("custom-resource-name");
    }

    [Test]
    public void RestResource_Id_CanBeAssignedAndRetrieved()
    {
        var res = new DeepRestItemResource
        {
            Id = 42,
            Title = "Sample Item",
        };

        res.Id.Should().Be(42);
        res.Title.Should().Be("Sample Item");
    }

    [Test]
    public void RestResource_JsonSerialization_IgnoresDefaultIdAndSerializesAssignedId()
    {
        // Default Id = 0 should be omitted from JSON
        var resDefault = new DeepRestItemResource
        {
            Id = 0,
            Title = "Omitted Id",
        };
        var jsonDefault = JsonSerializer.Serialize(resDefault);
        jsonDefault.Should().NotContain("\"id\":");
        jsonDefault.Should().NotContain("\"Id\":");
        jsonDefault.Should().NotContain("resourceName");

        // Non-default Id should be included in JSON
        var resAssigned = new DeepRestItemResource
        {
            Id = 100,
            Title = "Included Id",
        };
        var jsonAssigned = JsonSerializer.Serialize(resAssigned);
        jsonAssigned.Should().Contain("\"id\":100");
        jsonAssigned.Should().NotContain("resourceName");

        // Deserialization round-trip
        var deserialized = JsonSerializer.Deserialize<DeepRestItemResource>("{\"id\":999,\"title\":\"Roundtrip\"}");
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(999);
        deserialized.Title.Should().Be("Roundtrip");
        deserialized.ResourceName.Should().Be("deeprestitem");
    }

    [Test]
    public void RestPutByIdAttribute_PropertiesAndAttributeUsage_Valid()
    {
        var attr = new RestPutByIdAttribute();
        attr.Template.Should().Be("{id:int?}");
        attr.HttpMethods.Should().Contain("PUT");

        var usage = (AttributeUsageAttribute)typeof(RestPutByIdAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), true)
            .Single();
        usage.ValidOn.Should().Be(AttributeTargets.Method);

        var method = typeof(DeepSampleRestController).GetMethod("UpdateResource");
        method.Should().NotBeNull();

        var methodAttr = method!.GetCustomAttribute<RestPutByIdAttribute>();
        methodAttr.Should().NotBeNull();
        methodAttr!.Template.Should().Be("{id:int?}");
    }

    // ------------------------------------------------------------------------
    // Part 3: Live End-to-End HTTP Integration Tests
    // ------------------------------------------------------------------------

    [Test]
    public async Task LiveApi_SystemStatusEndpoint_ReturnsValidBuildAndEnvironmentInfo()
    {
        var response = await this.Client.GetAsync("/api/v1/system/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("appName").GetString().Should().Be("Leecharr");
        root.GetProperty("version").GetString().Should().Be(BuildInfo.Version.ToString());
        root.GetProperty("branch").GetString().Should().Be("main");
        root.GetProperty("isProduction").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task LiveApi_ApiKeyInQueryParam_AuthenticatesSuccessfully()
    {
        using var client = new HttpClient { BaseAddress = new Uri(GlobalSetup.Factory.BaseUrl) };
        var response = await client.GetAsync($"/api/v1/system/status?apikey={this.ApiKey}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task LiveApi_ApiKeyInBearerHeader_AuthenticatesSuccessfully()
    {
        using var client = new HttpClient { BaseAddress = new Uri(GlobalSetup.Factory.BaseUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", this.ApiKey);
        var response = await client.GetAsync("/api/v1/system/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task LiveApi_InvalidApiKey_ReturnsUnauthorized()
    {
        using var client = new HttpClient { BaseAddress = new Uri(GlobalSetup.Factory.BaseUrl) };
        client.DefaultRequestHeaders.Add("X-Api-Key", "invalid_api_key_for_testing_12345");
        var response = await client.GetAsync("/api/v1/system/status");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
