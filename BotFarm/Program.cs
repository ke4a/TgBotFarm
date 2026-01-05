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
        await InitializeBots(host);

        var jobRegistry = new ScheduledJobsRegistry(
            host.Services.GetService<IBackupService>()!,
            host.Services.GetServices<BotRegistration>(),
            host.Services.GetService<IHostApplicationLifetime>()!,
            host.Services.GetService<IConfiguration>()!,
            host.Services.GetService<ILogger<ScheduledJobsRegistry>>()!);
        var jobs = jobRegistry.GetJobs();
        jobs.Start();

        await host.RunAsync();
    }

    private static async Task InitializeBots(IHost host)
    {
        var configService = host.Services.GetService<IConfiguration>();
        var webHookUrl = configService.GetValue<string>("WebHookUrl");
        var botServices = host.Services.GetServices<IBotService>();

        foreach (var botService in botServices)
        {
            await botService.Initialize();

            if (webHookUrl == "devtunnel")
            {
                // Run BotFarm project with Visual Studio dev tunnel
                // https://learn.microsoft.com/en-us/aspnet/core/test/dev-tunnels?view=aspnetcore-8.0
                var devTunnel = Environment.GetEnvironmentVariable("VS_TUNNEL_URL")?.TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(devTunnel))
                {
                    await botService.InitializeWebHook($"{devTunnel}/api/{botService.Name}/update");
                }
                else
                {
                    throw new Exception("Could not get tunnel URL. Ensure VS dev tunnel is active.");
                }
            }
            else if (webHookUrl == "docker")
            {
                // Run docker-compose project with localtunnel
                // https://theboroer.github.io/localtunnel-www
                var localTunnel = Environment.GetEnvironmentVariable("LOCALTUNNEL_URL")?.TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(localTunnel))
                {
                    await botService.InitializeWebHook($"{localTunnel}/api/{botService.Name}/update");
                }
                else
                {
                    throw new Exception("Could not get localtunnel URL. Ensure LOCALTUNNEL_URL is set.");
                }
            }
            else
            {
                await botService.InitializeWebHook($"{webHookUrl}/api/{botService.Name}/update");
            }
        }
    }

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
            });
}
