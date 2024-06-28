using BotFarm.Core.Extensions;using BotFarm.Core.Services.Interfaces;
using BotFarm.Extensions;
using HealthChecks.UI.Client;
using HealthChecks.UI.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
#if !DEBUG
using System.Collections.Generic;
using System.Security.Claims;
using ZNetCS.AspNetCore.Authentication.Basic;
using ZNetCS.AspNetCore.Authentication.Basic.Events; 
#endif

namespace BotFarm
{
    public class Startup
    {
        private const string HEALTH_CHECKS_UI_POLICY = nameof(HEALTH_CHECKS_UI_POLICY);

        public IConfiguration Configuration { get; }

        public Startup(IWebHostEnvironment env)
        {
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
            services.AddControllers().AddNewtonsoftJson();
            services.AddRazorPages();

            services.AddCoreServices(Configuration)
                    .AddTestBotServices(Configuration);

            services.ConfigureHealthChecks(Configuration)
                    .AddTestBotHealthChecks();

#if !DEBUG
            services
                .AddAuthorization(cfg =>
                {
                    cfg.AddPolicy(name: HEALTH_CHECKS_UI_POLICY, cfgPolicy =>
                    {
                        cfgPolicy.AddRequirements().RequireAuthenticatedUser();
                        cfgPolicy.AddAuthenticationSchemes(BasicAuthenticationDefaults.AuthenticationScheme);
                    });
                })
                .AddAuthentication(BasicAuthenticationDefaults.AuthenticationScheme)
                .AddBasicAuthentication(
                    options =>
                    {
                        options.Realm = "My Application";
                        options.Events = new BasicAuthenticationEvents
                        {
                            OnValidatePrincipal = context =>
                            {
                                var settings = Configuration.GetSection(nameof(AuthenticationConfig)).Get<AuthenticationConfig>();
                                if ((context.UserName.Equals(settings.AdminUser, System.StringComparison.InvariantCulture))
                                    && (context.Password.Equals(settings.AdminPassword, System.StringComparison.InvariantCulture)))
                                {
                                    var claims = new List<Claim>
                                    {
                                        new Claim(ClaimTypes.Name, context.UserName, context.Options.ClaimsIssuer)
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
#endif
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
                })
#if !DEBUG
                .RequireAuthorization(HEALTH_CHECKS_UI_POLICY);
#else
                ;
#endif
                endpoints.MapHealthChecksUI(delegate (Options options)
                {
                    options.UIPath = "/health-ui";
                    options.AddCustomStylesheet("wwwroot/css/health.css");
                })
#if !DEBUG
                .RequireAuthorization(HEALTH_CHECKS_UI_POLICY);
#else
                ;
#endif
            });
            appLifetime.ApplicationStopping.Register(() =>
            {
                logger.LogWarning("Hosting environment initiated shutdown.");
                foreach (var dbService in dbServices)
                {
                    _ = Task.Run(async () => await dbService.Release()).Result;
                }
            });
        }
    }
}
