using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Leecharr.Http;

public class VersionedApiControllerAttribute : ApiControllerAttribute, IRouteTemplateProvider
{
    private readonly string _resource;

    protected VersionedApiControllerAttribute(string resource, int version)
    {
        _resource = resource;
        Version = version;
    }

    public int Version { get; }
    public string Template => $"api/v{Version}/{_resource}";
    public int? Order => 0;
    public string Name { get; set; }
}

public class V1ApiControllerAttribute : VersionedApiControllerAttribute
{
    public V1ApiControllerAttribute(string resource)
        : base(resource, 1)
    {
    }
}
