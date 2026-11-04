using AspNetCore.Identity.MongoDbCore.Extensions;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using BotFarm.Authentication;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Services;
using BotFarm.HostedServices;
using HealthChecks.UI.Client;
using HealthChecks.UI.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using MudBlazor;
using MudBlazor.Services;
using Telegram.Bot.AspNetCore;

namespace BotFarm.Extensions;

/// <summary>
/// Composes the BotFarm web application: its services, middleware pipeline and endpoints.
/// </summary>
public static class WebApplicationExtensions
{
    private const string HEALTH_CHECKS_UI_POLICY = nameof(HEALTH_CHECKS_UI_POLICY);

    /// <summary>
    /// Registers BotFarm's services: data protection, MVC, Razor Pages and components, MudBlazor,
    /// hosted services, core and bot services, health checks, MongoDB Identity, authorization and authentication.
    /// </summary>
    /// <remarks>
    /// Bots are found through the <see cref="IBotModule"/> each referenced bot project declares with
    /// <see cref="Core.Base.BotModuleAttribute"/>.
    /// Each module is registered as an <see cref="IBotModule"/> singleton and adds its own services and health checks.
    /// </remarks>
    public static WebApplicationBuilder AddBotFarm(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var environment = builder.Environment;
        var isDevelopment = environment.IsDevelopment();

        // Generated once per process lifetime; used only for internal calls (/health).
        var internalApiKey = Guid.NewGuid().ToString("N");

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(new AppDataPaths(configuration).DataProtectionKeys))
            .SetApplicationName("BotFarm");
        services.AddControllersWithViews();
        services.ConfigureTelegramBotMvc();
        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToFolder("/Account");
        });
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddCascadingAuthenticationState();
        services.AddHttpContextAccessor();
        services.AddHttpClient();

        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.MaximumOpacity = 100;
        });

        services.AddSingleton<SetupCodeService>();
        services.AddHostedService<SetupAnnouncementHostedService>();
        services.AddHostedService<BotControlStartupService>();
        services.AddHostedService<NightlyBackupService>();
        services.AddHostedService<PeriodicShutdownService>();

        services.AddCoreServices(configuration, environment);

        var botModules = BotModuleDiscovery.Discover(typeof(Program).Assembly);
        foreach (var botModule in botModules)
        {
            services.AddSingleton(botModule);
            botModule.AddServices(services, configuration);
        }

        var healthChecks = services.ConfigureHealthChecks(internalApiKey)
            .AddHealthChecks();
        foreach (var botModule in botModules)
        {
            botModule.AddHealthChecks(healthChecks);
        }

        var mongoIdentityConfig = new MongoDbIdentityConfiguration
        {
            MongoDbSettings = new MongoDbSettings
            {
                ConnectionString = configuration.GetConnectionString("MongoDb"),
                DatabaseName = "BotFarmIdentity"
            },
            IdentityOptionsAction = options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.User.RequireUniqueEmail = false;
            }
        };
        services.ConfigureMongoDbIdentity<ApplicationUser>(mongoIdentityConfig)
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddAuthorizationBuilder()
            .AddPolicy(name: HEALTH_CHECKS_UI_POLICY, cfgPolicy =>
            {
                cfgPolicy.RequireAuthenticatedUser();
                cfgPolicy.AddAuthenticationSchemes(
                    isDevelopment ? DevelopmentAuthenticationDefaults.Scheme : IdentityConstants.ApplicationScheme,
                    ApiKeyAuthenticationDefaults.Scheme);
            });

        var authenticationBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = isDevelopment
                ? DevelopmentAuthenticationDefaults.Scheme
                : IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = options.DefaultScheme;
        });

        authenticationBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationDefaults.Scheme,
            options => options.ApiKey = internalApiKey);

        authenticationBuilder.AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/Login";
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
        });

        if (isDevelopment)
        {
            authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationDefaults.Scheme,
                _ => { });
        }

        return builder;
    }

    /// <summary>
    /// Configures BotFarm's middleware pipeline and maps its pages, controllers, health endpoints and Razor components,
    /// including the pages of every registered bot module.
    /// </summary>
    public static WebApplication UseBotFarm(this WebApplication app)
    {
        var botModules = app.Services.GetServices<IBotModule>().ToList();
        app.Logger.LogInformation("Bot modules loaded: {BotModules}",
            string.Join(", ", botModules.Select(module => module.Name)));

        // WebApplication adds the developer exception page first in Development.
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapRazorPages();
        app.MapControllers();
        // Liveness probe for Docker and other supervisors: runs no checks and reveals no details,
        // so it is safe to leave anonymous.
        app.MapHealthChecks("/healthz", new HealthCheckOptions
        {
            Predicate = _ => false
        }).AllowAnonymous();
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
        }).RequireAuthorization(HEALTH_CHECKS_UI_POLICY);
        app.MapHealthChecksUI(delegate(Options options)
        {
            options.UIPath = "/health-ui";
            options.AddCustomStylesheet("wwwroot/css/health.css");
        }).RequireAuthorization(HEALTH_CHECKS_UI_POLICY);
        app.MapRazorComponents<App>()
            .AddAdditionalAssemblies(BotModuleDiscovery.GetComponentsAssemblies(botModules))
            .AddInteractiveServerRenderMode()
            .RequireAuthorization();

        return app;
    }
}
