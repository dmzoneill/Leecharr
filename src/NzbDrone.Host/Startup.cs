// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DryIoc;
using Leecharr.Http.Authentication;
using Leecharr.Http.Security;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
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

        services.AddSignalR();
        services.AddDataProtection();
        services.AddHttpClient();
        services.AddSingleton<Leecharr.Http.Terminal.IPtyTerminalService, Leecharr.Http.Terminal.PtyTerminalService>();

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

                // 1. API Key present in header or query parameter
                if (req.Headers.ContainsKey("X-Api-Key") || req.Query.ContainsKey("apikey"))
                {
                    return ApiKeyAuthenticationOptions.DefaultScheme;
                }

                // 2. HTTP Basic Auth header
                if (req.Headers.ContainsKey("Authorization") && req.Headers["Authorization"].ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    return BasicAuthenticationOptions.DefaultScheme;
                }

                // 3. Forward-Auth reverse proxy headers
                if (req.Headers.ContainsKey("Remote-User") ||
                    req.Headers.ContainsKey("X-authentik-username") ||
                    req.Headers.ContainsKey("X-Forwarded-User"))
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
            options.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/api") ||
                    ctx.Request.Path.StartsWithSegments("/signalr") ||
                    ctx.Request.Path.StartsWithSegments("/transmission") ||
                    ctx.Request.Path.StartsWithSegments("/json") ||
                    ctx.Request.Path.StartsWithSegments("/gui") ||
                    ctx.Request.Path.StartsWithSegments("/rpc") ||
                    ctx.Request.Path.StartsWithSegments("/RPC2") ||
                    ctx.Request.Path.StartsWithSegments("/RPC1") ||
                    ctx.Request.Path.StartsWithSegments("/webapi") ||
                    ctx.Request.Path.StartsWithSegments("/jsonrpc") ||
                    ctx.Request.Path.StartsWithSegments("/nzbget") ||
                    ctx.Request.Path.StartsWithSegments("/hadouken") ||
                    ctx.Request.Path.StartsWithSegments("/sabnzbd") ||
                    ctx.Request.Path.StartsWithSegments("/aria2"))
                {
                    ctx.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
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
    }

    public void Configure(WebApplication app)
    {
        var configFileProvider = app.Services.GetRequiredService<IConfigFileProvider>();
        if (!string.IsNullOrWhiteSpace(configFileProvider.UrlBase))
        {
            var urlBase = configFileProvider.UrlBase.Trim();
            if (!urlBase.StartsWith('/'))
            {
                urlBase = "/" + urlBase;
            }

            app.UsePathBase(urlBase);
        }

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        });

        app.UseMiddleware<SecurityHeadersMiddleware>();

        app.UseCors();

        app.UseMiddleware<HostHeaderValidationMiddleware>();
        app.UseMiddleware<CsrfProtectionMiddleware>();

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

        app.Map("/api/v1/terminal/ws", async context =>
        {
            var ptyService = context.RequestServices.GetRequiredService<Leecharr.Http.Terminal.IPtyTerminalService>();
            var configService = context.RequestServices.GetRequiredService<IConfigService>();
            await Leecharr.Http.Terminal.TerminalWebSocketHandler.HandleWebSocket(context, ptyService, configService);
        });

        app.MapFallbackToFile("index.html");
    }
}
