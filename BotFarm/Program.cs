using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using BotFarm.Core.Services;
using FluentScheduler;
using Microsoft.Extensions.Hosting;
using NLog.Web;

namespace BotFarm;

public class Program
{
    public static DateTime StartTime { get; private set; }

    public static async Task Main(string[] args)
    {
        StartTime = DateTime.UtcNow;
        using var host = CreateHostBuilder(args).Build();

        var jobRegistry = new ScheduledJobsRegistry(
            host.Services.GetService<IBackupService>()!,
            host.Services.GetServices<BotIdentity>(),
            host.Services.GetService<IHostApplicationLifetime>()!,
            host.Services.GetService<IConfiguration>()!,
            host.Services.GetService<ILogger<ScheduledJobsRegistry>>()!);
        var jobs = jobRegistry.GetJobs();
        jobs.Start();

        await host.StartAsync();

        var applicationLifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        try
        {
            await host.Services.GetRequiredService<BotControlCoordinator>()
                .InitializeAsync(applicationLifetime.ApplicationStopping);
        }
        catch (OperationCanceledException) when (applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            await host.WaitForShutdownAsync();
            return;
        }

        await host.WaitForShutdownAsync();
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
                services.Configure<HostOptions>(options => { options.ShutdownTimeout = TimeSpan.FromSeconds(25); });
            });
}