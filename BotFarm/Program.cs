using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using FluentScheduler;
using NLog.Web;

namespace BotFarm;

public class Program
{
    public static DateTime StartTime { get; private set; }

    public static async Task Main(string[] args)
    {
        StartTime = DateTime.UtcNow;
        var host = CreateHostBuilder(args).Build();

        var webhookInitializer = host.Services.GetRequiredService<IBotWebhookInitializer>();
        await webhookInitializer.InitializeAllAsync();

        var jobRegistry = new ScheduledJobsRegistry(
            host.Services.GetService<IBackupService>()!,
            host.Services.GetServices<BotIdentity>(),
            host.Services.GetService<IHostApplicationLifetime>()!,
            host.Services.GetService<IConfiguration>()!,
            host.Services.GetService<ILogger<ScheduledJobsRegistry>>()!);
        var jobs = jobRegistry.GetJobs();
        jobs.Start();

        await host.RunAsync();
    }

    /// <summary>
    /// Creates the web host builder with BotFarm's logging and shutdown defaults.
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStaticWebAssets()
                          .UseStartup<Startup>()
                          .ConfigureLogging(logging =>
                          {
                              logging.ClearProviders();
                              logging.SetMinimumLevel(LogLevel.Information);
                          })
                          .UseNLog();
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<HostOptions>(options =>
                {
                    options.ShutdownTimeout = TimeSpan.FromSeconds(25);
                });
            });
}
