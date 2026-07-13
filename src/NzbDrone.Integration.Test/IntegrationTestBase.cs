using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace NzbDrone.Integration.Test;

[TestFixture]
public abstract class IntegrationTestBase
{
    protected HttpClient Client => GlobalSetup.Factory.Client;

    protected string ApiKey => GlobalSetup.Factory.ApiKey;

    protected async Task<T> GetJsonAsync<T>(string path)
    {
        var response = await Client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    protected async Task<HttpResponseMessage> GetAsync(string path)
    {
        return await Client.GetAsync(path);
    }

    protected async Task<HttpResponseMessage> PostJsonAsync(string path, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PostAsync(path, content);
    }

    protected async Task<HttpResponseMessage> PutJsonAsync(string path, object body)
    {
        var json = JsonSerializer.Serialize(body);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await Client.PutAsync(path, content);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string path)
    {
        return await Client.DeleteAsync(path);
    }

    protected static T Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
