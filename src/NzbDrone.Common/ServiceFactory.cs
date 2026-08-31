// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using DryIoc;

namespace NzbDrone.Common;

public interface IServiceFactory
{
    T Build<T>()
        where T : class;

    object Build(Type type);

    IEnumerable<T> BuildAll<T>()
        where T : class;
}

public class ServiceFactory : IServiceFactory
{
    private readonly IResolver resolver;

    public ServiceFactory(IResolver resolver)
    {
        this.resolver = resolver;
    }

    public T Build<T>()
        where T : class
    {
        return this.resolver.Resolve<T>();
    }

    public object Build(Type type)
    {
        return this.resolver.Resolve(type);
    }

    public IEnumerable<T> BuildAll<T>()
        where T : class
    {
        return this.resolver.ResolveMany<T>();
    }
}
