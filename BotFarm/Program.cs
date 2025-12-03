using BotFarm.Core.Abstractions;
using BotFarm.Core.Models;
using FluentScheduler;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Options;
using NLog.Web;
//using Microsoft.Win32;

namespace BotFarm;

public class Program
{
    public static DateTime StartTime { get; private set; }

    public static async Task Main(string[] args)
    {
        #region Check NET versions installed on Windows server
        //var logger = NLogBuilder.ConfigureNLog("nlog.config").GetCurrentClassLogger();
        //var subkey = @"SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.NETCore.App";
        //var localKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        //var values = localKey.OpenSubKey(subkey).GetValueNames();
        //Array.Sort(values);
        //logger.Info($"Installed NET versions: {string.Join(" | ", values)}"); 
        #endregion

        StartTime = DateTime.UtcNow;
        var host = CreateWebHostBuilder(args).Build();
        await SetBotWebhooks(host);
        JobManager.Initialize(new ScheduledJobsRegistry(
            host.Services.GetService<IBackupService>(),
            host.Services.GetServices<IOptions<BotConfig>>(),
            host.Services.GetService<IHostApplicationLifetime>(),
            host.Services.GetService<IConfiguration>(),
            host.Services.GetService<ILogger<ScheduledJobsRegistry>>()));

        host.Run();
    }

    private static async Task SetBotWebhooks(IWebHost host)
    {
        var configService = host.Services.GetService<IConfiguration>();
        var webHookUrl = configService.GetValue<string>("WebHookUrl");
        var botServices = host.Services.GetServices<IBotService>();

        foreach (var botService in botServices)
        {
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

    public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .UseContentRoot(AppDomain.CurrentDomain.BaseDirectory)
            .UseStartup<Startup>()
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .UseNLog();
}
