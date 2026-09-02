// Copyright (c) PlaceholderCompany. All rights reserved.

using System;
using System.Reflection;
using System.Threading.Tasks;
using DryIoc;
using Leecharr.Http.Authentication;
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
                    ctx.Request.Path.StartsWithSegments("/json"))
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
                Title = "Leecharr API",
                Version = "v1",
                Description = "Servarr-Native BitTorrent & Media Downloader API",
            });
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

                        return uri.Host == "localhost" || uri.Host == "127.0.0.1" || uri.Host == "::1";
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

        app.UseCors();

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

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Leecharr API V1"));
        }

        app.MapControllers();
        app.MapHub<MessageHub>("/signalr/messages");

        app.MapFallbackToFile("index.html");
    }
}
