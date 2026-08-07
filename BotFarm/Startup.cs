using AspNetCore.Identity.MongoDbCore.Extensions;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using BotFarm.Authentication;
using BotFarm.Core.Abstractions;
using BotFarm.Core.Extensions;
using BotFarm.Core.Models;
using BotFarm.Extensions;
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
using TestBot.Extensions;

namespace BotFarm;

public class Startup
{
    private const string HEALTH_CHECKS_UI_POLICY = nameof(HEALTH_CHECKS_UI_POLICY);

    private readonly bool _isDevelopment;
    private readonly IWebHostEnvironment _environment;

    // Generated once per process lifetime; used only for internal calls (/health).
    private readonly string _internalApiKey = Guid.NewGuid().ToString("N");

    public IConfiguration Configuration { get; }

    public Startup(IWebHostEnvironment env)
    {
        _environment = env;
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
        services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dpkeys")))
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
        services.AddServerSideBlazor();
        services.AddHttpClient();

        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            config.SnackbarConfiguration.MaximumOpacity = 100;
        });

        services.AddCoreServices(Configuration, _environment)
                .AddTestBotServices(Configuration);

        services.AddHostedService<DatabaseShutdownHostedService>();

        services.ConfigureHealthChecks(_internalApiKey)
                .AddTestBotHealthChecks();

        var mongoIdentityConfig = new MongoDbIdentityConfiguration
        {
            MongoDbSettings = new MongoDbSettings
            {
                ConnectionString = Configuration.GetConnectionString("MongoDb"),
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
                    _isDevelopment ? DevelopmentAuthenticationDefaults.Scheme : IdentityConstants.ApplicationScheme,
                    ApiKeyAuthenticationDefaults.Scheme);
            });

        var authenticationBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = _isDevelopment ? DevelopmentAuthenticationDefaults.Scheme : IdentityConstants.ApplicationScheme;
            options.DefaultChallengeScheme = options.DefaultScheme;
        });

        authenticationBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationDefaults.Scheme,
            options => options.ApiKey = _internalApiKey);
        
        authenticationBuilder.AddCookie(IdentityConstants.ApplicationScheme, options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.AccessDeniedPath = "/Account/Login";
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
        });

        if (_isDevelopment)
        {
            authenticationBuilder.AddScheme<AuthenticationSchemeOptions, DevelopmentAuthenticationHandler>(
                DevelopmentAuthenticationDefaults.Scheme,
                _ => { });
        }
    }

    public void Configure(
        IApplicationBuilder app,
        IWebHostEnvironment env)
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
    }
}
