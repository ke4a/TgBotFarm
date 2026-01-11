using BotFarm.Authentication;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using BotFarm.Extensions;
using HealthChecks.UI.Client;
using HealthChecks.UI.Configuration;using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using MudBlazor;
using MudBlazor.Services;using System.Security.Claims;
using Telegram.Bot.AspNetCore;
using ZNetCS.AspNetCore.Authentication.Basic;
using ZNetCS.AspNetCore.Authentication.Basic.Events;

namespace BotFarm;

public class Startup
{
    private const string HEALTH_CHECKS_UI_POLICY = nameof(HEALTH_CHECKS_UI_POLICY);

    private readonly bool _isDevelopment;

    public IConfiguration Configuration { get; }

    public Startup(IWebHostEnvironment env)
    {
        _isDevelopment = env.IsDevelopment();

        var confBuilder = new ConfigurationBuilder()
            .SetBasePath(env.ContentRootPath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
        Configuration = confBuilder.Build();
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.ConfigureTelegramBotMvc();
        services.AddRazorPages();
        services.AddRazorComponents()
                .AddInteractiveServerComponents();
        services.AddServerSideBlazor();
        services.AddHttpClient();

        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.MaximumOpacity = 100;
        });

        services.AddCoreServices(Configuration);

        services.ConfigureHealthChecks(Configuration);

        services.AddAuthorizationBuilder()
            .AddPolicy(name: HEALTH_CHECKS_UI_POLICY, cfgPolicy =>
            {
                cfgPolicy.RequireAuthenticatedUser();
                cfgPolicy.AddAuthenticationSchemes(_isDevelopment ? DevelopmentAuthenticationDefaults.Scheme : BasicAuthenticationDefaults.AuthenticationScheme);
            });

        var authenticationBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = _isDevelopment ? DevelopmentAuthenticationDefaults.Scheme : BasicAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = options.DefaultScheme;
        });

        if (_isDevelopment)
        {
            authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationDefaults.Scheme,
                _ => { });
        }
        else
        {
            authenticationBuilder
                .AddBasicAuthentication(
                    options =>
                    {
                        options.Realm = "My Application";
                        options.Events = new BasicAuthenticationEvents
                        {
                            OnValidatePrincipal = context =>
                            {
                                var settings = Configuration.GetSection(nameof(AuthenticationConfig)).Get<AuthenticationConfig>();
                                if ((context.UserName.Equals(settings.AdminUser, StringComparison.InvariantCulture))
                                    && (context.Password.Equals(settings.AdminPassword, StringComparison.InvariantCulture)))
                                {
                                    var claims = new List<Claim>
                                    {
                                        new(ClaimTypes.Name, context.UserName, context.Options.ClaimsIssuer)
                                    };

                                    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, context.Scheme.Name));
                                    context.Principal = principal;
                                }
                                else
                                {
                                    context.AuthenticationFailMessage = "Authentication failed";
                                }

                                return Task.CompletedTask;
                            }
                        };
                    }
                );
        }
    }

    public void Configure(
        IApplicationBuilder app,
        IWebHostEnvironment env,
        IHostApplicationLifetime appLifetime,
        IEnumerable<IDatabaseService> dbServices,
        ILogger<Startup> logger)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapRazorPages();
            endpoints.MapControllers();
            endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            }).RequireAuthorization(HEALTH_CHECKS_UI_POLICY);
            endpoints.MapHealthChecksUI(delegate (Options options)
            {
                options.UIPath = "/health-ui";
                options.AddCustomStylesheet("wwwroot/css/health.css");
            }).RequireAuthorization(HEALTH_CHECKS_UI_POLICY);
            endpoints.MapBlazorHub().RequireAuthorization();
            endpoints.MapFallbackToPage("/_Host").RequireAuthorization();
        });
        appLifetime.ApplicationStopping.Register(() =>
        {
            logger.LogWarning("Hosting environment initiated shutdown.");
            foreach (var dbService in dbServices)
            {
                _ = Task.Run(async () => await dbService.Disconnect()).Result;
            }
        });
    }
}
