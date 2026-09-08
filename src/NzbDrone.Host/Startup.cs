// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DryIoc;
using Leecharr.Http.Authentication;
using Leecharr.Http.Security;
using Leecharr.Http.Terminal;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.SignalR;

namespace NzbDrone.Host;

public class Startup
{
    private readonly IContainer container;

    public Startup(IContainer container)
    {
        this.container = container;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        var apiAssembly = Assembly.Load("Leecharr.Api.V1");
        var httpAssembly = Assembly.Load("Leecharr.Http");

        services.AddControllers()
            .AddApplicationPart(apiAssembly)
            .AddApplicationPart(httpAssembly)
            .AddJsonOptions(options =>
            {
                var settings = STJson.GetSerializerSettings();
                options.JsonSerializerOptions.PropertyNamingPolicy = settings.PropertyNamingPolicy;
                options.JsonSerializerOptions.DefaultIgnoreCondition = settings.DefaultIgnoreCondition;
                foreach (var converter in settings.Converters)
                {
                    options.JsonSerializerOptions.Converters.Add(converter);
                }
            });

        services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            options.HandshakeTimeout = TimeSpan.FromSeconds(15);
            options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
        })
        .AddJsonProtocol(options =>
        {
            var settings = STJson.GetSerializerSettings();
            options.PayloadSerializerOptions.PropertyNamingPolicy = settings.PropertyNamingPolicy;
            options.PayloadSerializerOptions.DefaultIgnoreCondition = settings.DefaultIgnoreCondition;
            foreach (var converter in settings.Converters)
            {
                options.PayloadSerializerOptions.Converters.Add(converter);
            }
        });
        services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 250 * 1024 * 1024;
        });
        services.AddDataProtection();
        services.AddHttpClient();
        services.AddSingleton<Leecharr.Http.Terminal.IPtyTerminalService, Leecharr.Http.Terminal.PtyTerminalService>();
        services.AddScoped<Leecharr.Http.Authentication.ICookieSessionManager, Leecharr.Http.Authentication.CookieSessionManager>();
        services.AddScoped<Leecharr.Http.Authentication.CookieSessionAuthenticationEvents>();
        services.AddSingleton<NzbDrone.Core.Authentication.ISessionCleanupTask, NzbDrone.Core.Authentication.SessionCleanupTask>();

        var configFileProvider = this.container.Resolve<IConfigFileProvider>();
        if (configFileProvider.EnableSsl && configFileProvider.RedirectHttpToHttps)
        {
            services.AddHttpsRedirection(options =>
            {
                options.HttpsPort = configFileProvider.SslPort;
            });
        }

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "SmartAuth";
            options.DefaultChallengeScheme = "SmartAuth";
        })
        .AddPolicyScheme("SmartAuth", "Smart Authentication Router", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                var req = context.Request;
                var configFileProvider = context.RequestServices.GetService<NzbDrone.Core.Configuration.IConfigFileProvider>();

                // 0. When authentication is disabled, automatically grant local access
                if (configFileProvider != null && !configFileProvider.AuthenticationEnabled)
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 1. API Key present in header, query parameter, or Bearer token
                var hasApiKeyHeader = (req.Headers.TryGetValue("X-Api-Key", out var headerKey) && !string.IsNullOrWhiteSpace(headerKey)) ||
                                      (req.Headers.TryGetValue("ApiKey", out var headerKey2) && !string.IsNullOrWhiteSpace(headerKey2));
                var hasApiKeyQuery = (req.Query.TryGetValue("apikey", out var qKey) && !string.IsNullOrWhiteSpace(qKey)) ||
                                     (req.Query.TryGetValue("access_token", out var qToken) && !string.IsNullOrWhiteSpace(qToken)) ||
                                     (req.Query.TryGetValue("api_key", out var qApiKey) && !string.IsNullOrWhiteSpace(qApiKey));
                var hasBearerToken = req.Headers.TryGetValue("Authorization", out var authHeader) &&
                                     authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) &&
                                     !string.IsNullOrWhiteSpace(authHeader.ToString()["Bearer ".Length..].Trim());

                if (hasApiKeyHeader || hasApiKeyQuery || hasBearerToken)
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 2. HTTP Basic Auth header
                if (req.Headers.TryGetValue("Authorization", out var basicHeader) &&
                    basicHeader.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(basicHeader.ToString()["Basic ".Length..].Trim()))
                {
                    return BasicAuthenticationOptions.DefaultScheme;
                }

                // 3. Forward-Auth reverse proxy headers
                if ((req.Headers.TryGetValue("Remote-User", out var rUser) && !string.IsNullOrWhiteSpace(rUser)) ||
                    (req.Headers.TryGetValue("X-authentik-username", out var aUser) && !string.IsNullOrWhiteSpace(aUser)) ||
                    (req.Headers.TryGetValue("X-Forwarded-User", out var fUser) && !string.IsNullOrWhiteSpace(fUser)))
                {
                    return ForwardAuthOptions.DefaultScheme;
                }

                // 4. Default to Cookie authentication for interactive browser
                return "Cookies";
            };
        })
        .AddCookie("Cookies", options =>
        {
            options.Cookie.Name = "Leecharr_Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.LoginPath = "/login";
            options.AccessDeniedPath = "/login?accessDenied=true";
            options.EventsType = typeof(Leecharr.Http.Authentication.CookieSessionAuthenticationEvents);
        })
        .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme, _ => { })
        .AddScheme<BasicAuthenticationOptions, BasicAuthenticationHandler>(
            BasicAuthenticationOptions.DefaultScheme, _ => { })
        .AddScheme<ForwardAuthOptions, ForwardAuthHandler>(
            ForwardAuthOptions.DefaultScheme, _ => { });

        services.AddOptions<OpenIdConnectOptions>();
        services.AddSingleton<IDynamicAuthSchemeManager, DynamicAuthSchemeManager>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
            options.AddPolicy("RequireOperator", policy => policy.RequireRole("Admin", "Operator"));
            options.AddPolicy("RequireUser", policy => policy.RequireRole("Admin", "Operator", "User"));

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Leecharr REST API",
                Version = "v1",
                Description = "Servarr-Native BitTorrent & Media Downloader API Specification",
            });

            c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "Leecharr REST API Key header (X-Api-Key) or query parameter (apikey)",
                Name = "X-Api-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
            });

            c.AddSecurityRequirement(doc => new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecuritySchemeReference("ApiKey", doc),
                    new List<string>()
                },
            });

            c.ResolveConflictingActions(apiDescriptions => apiDescriptions.First());
            c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
        });

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder.SetIsOriginAllowed(origin =>
                    {
                        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                        {
                            return false;
                        }

                        var isLoopback = uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "::1";
                        if (!isLoopback)
                        {
                            return false;
                        }

                        return uri.Port == configFileProvider.Port ||
                               (configFileProvider.EnableSsl && uri.Port == configFileProvider.SslPort);
                    })
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        services.AddHostedService<AppLifetime>();
        services.AddHostedService<CommandWorker>();
    }

    public void Configure(WebApplication app)
    {
        var configFileProvider = app.Services.GetRequiredService<IConfigFileProvider>();
        var urlBase = configFileProvider.UrlBase?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(urlBase))
        {
            if (!urlBase.StartsWith('/'))
            {
                urlBase = "/" + urlBase;
            }

            app.UsePathBase(urlBase);
        }

        var forwardedOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        };
        forwardedOptions.KnownIPNetworks.Clear();
        forwardedOptions.KnownProxies.Clear();
        app.UseForwardedHeaders(forwardedOptions);

        app.UseMiddleware<SecurityHeadersMiddleware>();

        app.UseCors();

        app.UseMiddleware<HostHeaderValidationMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                var webRoot = app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
                var indexPath = Path.Combine(webRoot, "index.html");
                if (File.Exists(indexPath))
                {
                    var html = await File.ReadAllTextAsync(indexPath);
                    var effectiveUrlBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : urlBase;
                    var baseHref = string.IsNullOrEmpty(effectiveUrlBase) ? "/" : (effectiveUrlBase.EndsWith('/') ? effectiveUrlBase : effectiveUrlBase + "/");
                    var injection = $"<base href=\"{baseHref}\" /><script>window.Leecharr = {{ urlBase: \"{effectiveUrlBase}\" }};</script>";
                    if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                    {
                        html = html.Replace("<head>", $"<head>{injection}", StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        html = injection + html;
                    }

                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    await context.Response.WriteAsync(html);
                    return;
                }
            }

            await next();
        });

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
                {
                    ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                    ctx.Context.Response.Headers.Pragma = "no-cache";
                    ctx.Context.Response.Headers.Expires = "0";
                }
                else
                {
                    ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                }
            },
        });

        if (configFileProvider.EnableSsl && configFileProvider.RedirectHttpToHttps)
        {
            app.UseHttpsRedirection();
        }

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Leecharr REST API v1");
            c.RoutePrefix = "swagger";
            c.DocumentTitle = "Leecharr - REST API Docs";
            c.InjectStylesheet("/swagger-custom.css");
        });

        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.MapControllers();
        app.MapHub<MessageHub>("/signalr/messages");

        app.Use(async (context, next) =>
        {
            if ((context.Request.Path == "/ws/terminal" || context.Request.Path == "/api/v1/terminal/ws") &&
                context.WebSockets.IsWebSocketRequest)
            {
                var configFileProvider = context.RequestServices.GetRequiredService<IConfigFileProvider>();
                if (configFileProvider.AuthenticationEnabled)
                {
                    var isAuthenticated = (context.User?.Identity?.IsAuthenticated == true) ||
                                          RpcAuthenticationHelper.IsAuthenticated(context, configFileProvider);

                    if (!isAuthenticated)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Authentication required for terminal access.");
                        await context.Response.CompleteAsync();
                        return;
                    }
                }

                var ptyService = context.RequestServices.GetRequiredService<IPtyTerminalService>();
                var configService = context.RequestServices.GetRequiredService<IConfigService>();
                await TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService, configFileProvider);
                return;
            }

            await next();
        });

        var terminalHandler = async (HttpContext context) =>
        {
            var configFileProvider = context.RequestServices.GetRequiredService<IConfigFileProvider>();
            if (configFileProvider.AuthenticationEnabled)
            {
                var isAuthenticated = (context.User?.Identity?.IsAuthenticated == true) ||
                                      RpcAuthenticationHelper.IsAuthenticated(context, configFileProvider);

                if (!isAuthenticated)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Authentication required for terminal access.");
                    await context.Response.CompleteAsync();
                    return;
                }
            }

            var ptyService = context.RequestServices.GetRequiredService<IPtyTerminalService>();
            var configService = context.RequestServices.GetRequiredService<IConfigService>();
            await TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService, configFileProvider);
        };

        app.Map("/ws/terminal", terminalHandler);
        app.Map("/api/v1/terminal/ws", terminalHandler);

        app.MapFallback(async context =>
        {
            var webRoot = app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var indexPath = Path.Combine(webRoot, "index.html");
            if (File.Exists(indexPath))
            {
                var html = await File.ReadAllTextAsync(indexPath);
                var effectiveUrlBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : urlBase;
                var baseHref = string.IsNullOrEmpty(effectiveUrlBase) ? "/" : (effectiveUrlBase.EndsWith('/') ? effectiveUrlBase : effectiveUrlBase + "/");
                var injection = $"<base href=\"{baseHref}\" /><script>window.Leecharr = {{ urlBase: \"{effectiveUrlBase}\" }};</script>";
                if (html.Contains("<head>", StringComparison.OrdinalIgnoreCase))
                {
                    html = html.Replace("<head>", $"<head>{injection}", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    html = injection + html;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                await context.Response.WriteAsync(html);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            }
        });
    }
}
