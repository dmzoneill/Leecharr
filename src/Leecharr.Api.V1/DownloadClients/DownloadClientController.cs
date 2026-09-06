// Copyright (c) PlaceholderCompany. All rights reserved.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Leecharr.Api.V1.ArrIntegration;
using Leecharr.Api.V1.Torrents;
using Leecharr.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Core.DownloadClients;
using NzbDrone.Core.Torrents;

namespace Leecharr.Api.V1.DownloadClients;

[V1ApiController("downloadclients")]
[Authorize(Policy = "RequireOperator")]
public class DownloadClientController : Controller
{
    private readonly IDownloadClientRepository repository;
    private readonly ITorrentService torrentService;
    private readonly HttpClient httpClient;
    private readonly Logger logger = LogManager.GetCurrentClassLogger();

    public DownloadClientController(IDownloadClientRepository repository, ITorrentService torrentService, HttpClient httpClient = null)
    {
        this.repository = repository;
        this.torrentService = torrentService;
        this.httpClient = httpClient;
    }

    [HttpGet]
    public ActionResult<List<DownloadClientResource>> GetAll()
    {
        var definitions = this.repository.All();
        return this.Ok(definitions.Select(ToResource).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<DownloadClientResource> Get(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return this.Ok(ToResource(definition));
    }

    [HttpPost]
    public ActionResult<DownloadClientResource> Create([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var model = ToModel(resource);
        var created = this.repository.Insert(model);
        return this.Ok(ToResource(created));
    }

    [HttpPut("{id:int}")]
    public ActionResult<DownloadClientResource> Update(int id, [FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var existing = this.repository.Get(id);
        if (existing == null)
        {
            return this.NotFound();
        }

        var model = ToModel(resource);
        model.Id = id;
        if (string.IsNullOrEmpty(model.Password) || model.Password.Contains('*'))
        {
            model.Password = existing.Password;
        }

        this.repository.Update(model);
        return this.Ok(ToResource(model));
    }

    [HttpDelete("{id:int}")]
    public ActionResult Delete(int id)
    {
        this.repository.Delete(id);
        return this.Ok();
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<DownloadClientTestResult>> Test(int id)
    {
        var definition = this.repository.Get(id);
        if (definition == null)
        {
            return this.NotFound();
        }

        return await this.TestDirectInternal(ToResource(definition), definition.Password);
    }

    [HttpPost("test")]
    public async Task<ActionResult<DownloadClientTestResult>> TestDirect([FromBody] DownloadClientResource resource)
    {
        if (resource == null)
        {
            return this.BadRequest();
        }

        var password = resource.Password;
        if ((string.IsNullOrEmpty(password) || password.Contains('*')) && resource.Id > 0)
        {
            var existing = this.repository.Get(resource.Id);
            if (existing != null)
            {
                password = existing.Password;
            }
        }

        return await this.TestDirectInternal(resource, password);
    }

    [HttpGet("{id:int}/items")]
    public async Task<ActionResult<List<DownloadClientRemoteItem>>> GetItems(int id)
    {
        var client = this.repository.Get(id);
        if (client == null)
        {
            return this.NotFound();
        }

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
        return this.Ok(items);
    }

    [HttpPost("{id:int}/import/{hash}")]
    public async Task<ActionResult<TorrentResource>> ImportTorrent(int id, string hash)
    {
        var client = this.repository.Get(id);
        if (client == null)
        {
            return this.NotFound();
        }

        var existing = this.torrentService.GetByInfoHash(hash);
        if (existing != null)
        {
            return this.Ok(TorrentResourceMapper.ToResource(existing));
        }

        var items = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
        var remoteItem = items.FirstOrDefault(i => string.Equals(i.InfoHash, hash, StringComparison.OrdinalIgnoreCase));
        var savePath = !string.IsNullOrWhiteSpace(remoteItem?.SavePath) ? remoteItem.SavePath : null;
        var category = !string.IsNullOrWhiteSpace(remoteItem?.Category) ? remoteItem.Category : client.Category;

        // Add magnet by hash to engine
        var magnetUri = $"magnet:?xt=urn:btih:{hash}";
        var added = await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, false);
        return this.Ok(TorrentResourceMapper.ToResource(added));
    }

    [HttpPost("{id:int}/import")]
    public async Task<ActionResult<SyncResultResource>> ImportTorrents(int id, [FromBody] ImportRequest request)
    {
        var client = this.repository.Get(id);
        if (client == null)
        {
            return this.NotFound();
        }

        var count = 0;
        var hashes = request?.EffectiveHashes?.ToList();
        if (hashes != null && hashes.Count > 0)
        {
            var remoteItems = await DownloadClientRemoteQuery.QueryRemoteClientItemsAsync(client, this.httpClient);
            var remoteItemMap = remoteItems
                .Where(i => !string.IsNullOrEmpty(i.InfoHash))
                .GroupBy(i => i.InfoHash, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var hash in hashes)
            {
                try
                {
                    var existing = this.torrentService.GetByInfoHash(hash);
                    if (existing == null)
                    {
                        remoteItemMap.TryGetValue(hash, out var remoteItem);
                        var savePath = !string.IsNullOrWhiteSpace(request?.SavePath)
                            ? request.SavePath
                            : (!string.IsNullOrWhiteSpace(remoteItem?.SavePath) ? remoteItem.SavePath : null);
                        var category = !string.IsNullOrWhiteSpace(request?.Category)
                            ? request.Category
                            : (!string.IsNullOrWhiteSpace(remoteItem?.Category) ? remoteItem.Category : client.Category);

                        var magnetUri = $"magnet:?xt=urn:btih:{hash}";
                        await this.torrentService.AddFromMagnetAsync(magnetUri, category, savePath, request?.StartPaused ?? false);
                        count++;
                    }
                }
                catch
                {
                }
            }
        }

        return this.Ok(new SyncResultResource
        {
            Success = true,
            SyncedCount = count,
            Added = count,
            Message = $"Import completed ({count} torrent(s) imported).",
        });
    }

    private static DownloadClientResource ToResource(DownloadClientDefinition model)
    {
        return new DownloadClientResource
        {
            Id = model.Id,
            Name = model.Name,
            ClientType = model.ClientType,
            Host = model.Host,
            Port = model.Port,
            UseSsl = model.UseSsl,
            Username = model.Username,
            Password = string.IsNullOrEmpty(model.Password) ? string.Empty : "********",
            Category = model.Category,
            Enabled = model.Enable,
        };
    }

    private static DownloadClientDefinition ToModel(DownloadClientResource resource)
    {
        return new DownloadClientDefinition
        {
            Id = resource.Id,
            Name = resource.Name,
            ClientType = resource.ClientType ?? "qBittorrent",
            Host = resource.Host ?? "localhost",
            Port = resource.Port > 0 ? resource.Port : 8080,
            UseSsl = resource.UseSsl,
            Username = resource.Username,
            Password = resource.Password,
            Category = resource.Category,
            Enable = resource.Enabled,
        };
    }

    private async Task<ActionResult<DownloadClientTestResult>> TestDirectInternal(DownloadClientResource resource, string passwordOverride = null)
    {
        if (string.IsNullOrWhiteSpace(resource.Host))
        {
            return this.Ok(new DownloadClientTestResult { Success = false, Message = "Host is required." });
        }

        var port = resource.Port > 0 ? resource.Port : 8080;
        var scheme = resource.UseSsl ? "https" : "http";
        var baseUrl = $"{scheme}://{resource.Host}:{port}";
        var password = passwordOverride ?? resource.Password;

        HttpClient localHttp = null;
        if (this.httpClient == null)
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            };
            localHttp = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        }

        var http = this.httpClient ?? localHttp;

        try
        {
            if (string.Equals(resource.ClientType, "qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(resource.Username) || !string.IsNullOrWhiteSpace(password))
                {
                    var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "username", resource.Username ?? string.Empty },
                        { "password", password ?? string.Empty },
                    });

                    var loginResp = await http.PostAsync($"{baseUrl}/api/v2/auth/login", loginContent);
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        return this.Ok(new DownloadClientTestResult
                        {
                            Success = false,
                            Message = $"Authentication failed for qBittorrent at {baseUrl} (HTTP {(int)loginResp.StatusCode}).",
                        });
                    }

                    var loginResult = await loginResp.Content.ReadAsStringAsync();
                    if (string.Equals(loginResult.Trim(), "Fails.", StringComparison.OrdinalIgnoreCase))
                    {
                        return this.Ok(new DownloadClientTestResult
                        {
                            Success = false,
                            Message = $"Authentication failed for qBittorrent at {baseUrl}: Invalid username or password.",
                        });
                    }
                }

                var resp = await http.GetAsync($"{baseUrl}/api/v2/app/webapiVersion");
                if (resp.StatusCode == HttpStatusCode.Unauthorized || resp.StatusCode == HttpStatusCode.Forbidden)
                {
                    return this.Ok(new DownloadClientTestResult
                    {
                        Success = false,
                        Message = $"Authentication failed for qBittorrent at {baseUrl}: Access forbidden (credentials required).",
                    });
                }

                if (resp.IsSuccessStatusCode)
                {
                    var ver = await resp.Content.ReadAsStringAsync();
                    return this.Ok(new DownloadClientTestResult { Success = true, Message = $"qBittorrent WebAPI connected (v{ver.Trim()})." });
                }
            }
            else if (string.Equals(resource.ClientType, "Transmission", StringComparison.OrdinalIgnoreCase))
            {
                var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/transmission/rpc");
                if (!string.IsNullOrWhiteSpace(resource.Username) || !string.IsNullOrWhiteSpace(password))
                {
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{resource.Username}:{password}"));
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                }

                var resp = await http.SendAsync(req);
                if (resp.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return this.Ok(new DownloadClientTestResult
                    {
                        Success = false,
                        Message = $"Authentication failed for Transmission at {baseUrl}: Invalid username or password.",
                    });
                }

                if (resp.StatusCode == HttpStatusCode.Conflict || resp.IsSuccessStatusCode)
                {
                    return this.Ok(new DownloadClientTestResult { Success = true, Message = "Transmission RPC endpoint reachable." });
                }
            }
            else if (string.Equals(resource.ClientType, "Deluge", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(password))
                {
                    var loginContent = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            method = "auth.login",
                            @params = new object[] { password },
                            id = 1,
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var loginResp = await http.PostAsync($"{baseUrl}/json", loginContent);
                    if (!loginResp.IsSuccessStatusCode)
                    {
                        return this.Ok(new DownloadClientTestResult
                        {
                            Success = false,
                            Message = $"Authentication failed for Deluge at {baseUrl} (HTTP {(int)loginResp.StatusCode}).",
                        });
                    }

                    var loginJson = await loginResp.Content.ReadAsStringAsync();
                    using var loginDoc = JsonDocument.Parse(loginJson);
                    if (loginDoc.RootElement.TryGetProperty("result", out var resElem) &&
                        resElem.ValueKind == JsonValueKind.False)
                    {
                        return this.Ok(new DownloadClientTestResult
                        {
                            Success = false,
                            Message = $"Authentication failed for Deluge at {baseUrl}: Invalid password.",
                        });
                    }
                }

                var content = new StringContent("{\"method\":\"auth.check_session\",\"params\":[],\"id\":1}", Encoding.UTF8, "application/json");
                var resp = await http.PostAsync($"{baseUrl}/json", content);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("result", out var checkResult) && checkResult.ValueKind == JsonValueKind.True)
                    {
                        return this.Ok(new DownloadClientTestResult { Success = true, Message = "Deluge JSON-RPC connected successfully." });
                    }
                    else if (doc.RootElement.TryGetProperty("result", out var falseRes) && falseRes.ValueKind == JsonValueKind.False)
                    {
                        if (string.IsNullOrWhiteSpace(password))
                        {
                            return this.Ok(new DownloadClientTestResult { Success = false, Message = "Authentication failed for Deluge: Password required." });
                        }
                    }

                    return this.Ok(new DownloadClientTestResult { Success = true, Message = "Deluge JSON-RPC endpoint reachable." });
                }
            }

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(resource.Host, port, cts.Token);
            return this.Ok(new DownloadClientTestResult
            {
                Success = true,
                Message = $"Connected to {resource.ClientType ?? "Client"} socket at {resource.Host}:{port} successfully.",
            });
        }
        catch (Exception ex)
        {
            this.logger.Warn(ex, "TestDirectInternal failed for {0}:{1}", resource.Host, port);
            return this.Ok(new DownloadClientTestResult
            {
                Success = false,
                Message = $"Failed to connect to {resource.Host}:{port} - {ex.Message}",
            });
        }
        finally
        {
            localHttp?.Dispose();
        }
    }
}
