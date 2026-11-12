using AspNetCore.Identity.MongoDbCore.Extensions;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using BotFarm.Authentication;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Controllers;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using BotFarm.Health;
using BotFarm.HostedServices;
using BotFarm.Localization;
using BotFarm.Shared.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.WebEncoders;
using MudBlazor;
using MudBlazor.Services;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Telegram.Bot.AspNetCore;

namespace BotFarm.Extensions;

/// <summary>
/// Composes the BotFarm web application: its services, middleware pipeline and endpoints.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Registers BotFarm's services: data protection, MVC, Razor Pages and components, an HTML encoder that leaves
    /// Cyrillic text unescaped, MudBlazor and its translations, hosted services, core and bot services, admin UI
    /// localization, health checks, MongoDB Identity, authorization and authentication.
    /// </summary>
    /// <remarks>
    /// Bots are found through the <see cref="IBotModule"/> each referenced bot project declares with
    /// <see cref="Core.Base.BotModuleAttribute"/>.
    /// Each module is registered as an <see cref="IBotModule"/> singleton and adds its own services and health checks.
    /// Each bot's <see cref="BotIdentity"/> is registered as a singleton keyed by the bot name.
    /// </remarks>
    public static WebApplicationBuilder AddBotFarm(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var environment = builder.Environment;
        var isDevelopment = environment.IsDevelopment();

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
        // Razor Pages encode text with the registered HtmlEncoder, which by default escapes every character outside
        // Basic Latin; allowing Cyrillic and the typographic quotes, apostrophe and ellipsis the translations use
        // (Latin-1 Supplement, General Punctuation) keeps the translated sign-in and setup pages readable in their HTML source.
        services.Configure<WebEncoderOptions>(options =>
            options.TextEncoderSettings = new TextEncoderSettings(
                UnicodeRanges.BasicLatin,
                UnicodeRanges.Latin1Supplement,
                UnicodeRanges.GeneralPunctuation,
                UnicodeRanges.Cyrillic));
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
        services.AddTransient<MudLocalizer, AdminMudLocalizer>();

        services.AddSingleton<SetupCodeService>();
        services.AddHostedService<SetupAnnouncementHostedService>();
        services.AddHostedService<BotControlStartupService>();
        services.AddHostedService<NightlyBackupService>();
        services.AddHostedService<PeriodicShutdownService>();

        services.AddCoreServices(configuration, environment);
        services.AddAdminLocalization();

        var botModules = BotModuleDiscovery.Discover(typeof(Program).Assembly);
        foreach (var botModule in botModules)
        {
            services.AddSingleton(botModule);
            services.AddKeyedSingleton(botModule.Name, new BotIdentity(botModule.Name));
            botModule.AddServices(services, configuration);
        }

        var healthChecks = services.ConfigureHealthChecks()
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
            .AddDefaultTokenProviders()
            .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

        services.AddAuthorization();

        var userScheme = isDevelopment
            ? DevelopmentAuthenticationDefaults.Scheme
            : IdentityConstants.ApplicationScheme;
        var authenticationBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = userScheme;
            options.DefaultChallengeScheme = userScheme;
        });
        authenticationBuilder.AddHealthApiKey(configuration, userScheme);

        authenticationBuilder.AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/Login";
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.AnswerHealthWithUnauthorized();
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
    /// Configures BotFarm's middleware pipeline and maps its pages, controllers, health endpoints, the admin UI language
    /// switch and Razor components, including the pages of every registered bot module.
    /// </summary>
    public static WebApplication UseBotFarm(this WebApplication app)
    {
        var botModules = app.Services.GetServices<IBotModule>().ToList();
        app.Logger.LogInformation("Bot modules loaded: {BotModules}",
            string.Join(", ", botModules.Select(module => module.Name)));

        // Load and validate the admin UI translation files now, so a broken or missing file stops startup
        // instead of failing the first admin request.
        var adminLanguages = app.Services.GetRequiredService<IAdminLanguageCatalog>();
        app.Logger.LogInformation("Admin UI languages loaded: {AdminLanguages}",
            string.Join(", ", adminLanguages.Languages.Select(language => language.Culture.Name)));

        // WebApplication adds the developer exception page first in Development.
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAdminLocalization();
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
        app.MapHealthChecks(HealthCheckExtensions.HealthPath, new HealthCheckOptions
        {
            ResponseWriter = HealthReportJsonWriter.WriteAsync
        }).RequireAuthorization(HealthCheckExtensions.HealthReaderPolicy);
        app.MapAdminLanguageEndpoints();
        app.MapRazorComponents<App>()
            .AddAdditionalAssemblies(BotModuleDiscovery.GetComponentsAssemblies(botModules))
            .AddInteractiveServerRenderMode()
            .RequireAuthorization();

        return app;
    }
}
