// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Composition;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace Leecharr.Core.Test;

[TestFixture]
public class ContainerLifecycleTest
{
    public interface ITestService
    {
        Guid Id { get; }
    }

    public interface IAnotherTestService
    {
        Guid Id { get; }
    }

    public class TestService : ITestService, IAnotherTestService
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public class StandaloneService
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    public interface IProxyEngine
    {
        Guid Id { get; }
    }

    public interface IProxyManager
    {
        Guid Id { get; }
    }

    public class MultiInterfaceProxy : IProxyEngine, IProxyManager, IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();

        public void Dispose()
        {
        }
    }

    public class EventOne : IEvent
    {
    }

    public class HandlerA : IHandle<EventOne>
    {
        public Guid Id { get; } = Guid.NewGuid();

        public void Handle(EventOne message)
        {
        }
    }

    public class HandlerB : IHandle<EventOne>
    {
        public Guid Id { get; } = Guid.NewGuid();

        public void Handle(EventOne message)
        {
        }
    }

    [Test]
    public void ResolvingInterfaceAndConcreteType_YieldsIdenticalSingletonReference()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        container.AutoAddServices(new List<string> { "Leecharr.Core", "Leecharr.Common" });

        var iface = container.Resolve<IConfigFileProvider>();
        var concrete = container.Resolve<ConfigFileProvider>();

        object.ReferenceEquals(iface, concrete).Should().BeTrue();
    }

    [Test]
    public void ResolvingMultipleInterfacesAndConcrete_YieldsIdenticalSingletonReference()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterSingletonWithInterfaces<TestService>();

        var iface1 = container.Resolve<ITestService>();
        var iface2 = container.Resolve<IAnotherTestService>();
        var concrete = container.Resolve<TestService>();

        object.ReferenceEquals(iface1, iface2).Should().BeTrue();
        object.ReferenceEquals(iface1, concrete).Should().BeTrue();
        object.ReferenceEquals(iface2, concrete).Should().BeTrue();
    }

    [Test]
    public void ResolvingConcreteClassWithoutInterface_YieldsIdenticalSingletonReference()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        container.Register(typeof(StandaloneService), Reuse.Singleton);

        var first = container.Resolve<StandaloneService>();
        var second = container.Resolve<StandaloneService>();

        object.ReferenceEquals(first, second).Should().BeTrue();
    }

    [Test]
    public void RegisterSingletonWithInterfaces_MultiInterfaceProxy_SharesSingleInstanceAcrossInterfacesAndConcrete()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterSingletonWithInterfaces<MultiInterfaceProxy>();

        var engine = container.Resolve<IProxyEngine>();
        var manager = container.Resolve<IProxyManager>();
        var concrete = container.Resolve<MultiInterfaceProxy>();

        object.ReferenceEquals(engine, manager).Should().BeTrue();
        object.ReferenceEquals(engine, concrete).Should().BeTrue();
    }

    [Test]
    public void RegisterSingleton_GenericHelper_SharesSingleInstanceBetweenInterfaceAndConcrete()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterSingleton<ITestService, TestService>();

        var iface = container.Resolve<ITestService>();
        var concrete = container.Resolve<TestService>();

        object.ReferenceEquals(iface, concrete).Should().BeTrue();
    }

    [Test]
    public void MultipleEventHandlersForSameEvent_AllResolvedAndMaintainSingletonIdentity()
    {
        var container = new Container(rules => rules.WithNzbDroneRules());
        container.RegisterSingletonWithInterfaces<HandlerA>(IfAlreadyRegistered.AppendNotKeyed);
        container.RegisterSingletonWithInterfaces<HandlerB>(IfAlreadyRegistered.AppendNotKeyed);

        var handlers = container.ResolveMany<IHandle<EventOne>>().ToList();
        handlers.Should().HaveCount(2);

        var handlerAFromMany = handlers.OfType<HandlerA>().Single();
        var concreteA = container.Resolve<HandlerA>();
        object.ReferenceEquals(handlerAFromMany, concreteA).Should().BeTrue();

        var handlerBFromMany = handlers.OfType<HandlerB>().Single();
        var concreteB = container.Resolve<HandlerB>();
        object.ReferenceEquals(handlerBFromMany, concreteB).Should().BeTrue();
    }
}
