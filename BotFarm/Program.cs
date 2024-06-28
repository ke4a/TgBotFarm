using BotFarm.Core.Models;
using BotFarm.Core.Services.Interfaces;
using FluentScheduler;
using Microsoft.AspNetCore;
using Microsoft.Extensions.Options;
using NLog.Web;
//using Microsoft.Win32;

namespace BotFarm
{
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
            await SetBotWebhook(host);
            JobManager.Initialize(new ScheduledJobsRegistry(host.Services.GetService<IBackupService>(), host.Services.GetServices<IOptions<BotConfig>>()));
            host.Run();
        }

        private static async Task SetBotWebhook(IWebHost host)
        {
            var configService = host.Services.GetService<IConfiguration>();
            var webHookUrl = configService.GetValue<string>("WebHookUrl");
            var botConfigs = configService.GetSection("Bots")
                               .GetChildren()
                               .Select(c => c.GetSection(nameof(BotConfig)).Get<BotConfig>());

            foreach (var bot in botConfigs)
            {
                var botService = host.Services.GetKeyedService<IBotService>(bot.Name);
                if (webHookUrl == "local")
                {
                    // https://learn.microsoft.com/en-us/aspnet/core/test/dev-tunnels?view=aspnetcore-8.0
                    var httpsTunnel = Environment.GetEnvironmentVariable("VS_TUNNEL_URL")?.TrimEnd('/');
                    if (!string.IsNullOrWhiteSpace(httpsTunnel))
                    {
                        await botService.InitializeWebHook($"{httpsTunnel}/api/{bot.Name}/update");
                    }
                    else
                    {
                        throw new Exception("Could not get tunnel URL.");
                    }
                }
                else
                {
                    await botService.InitializeWebHook($"{webHookUrl}/api/{bot.Name}/update");
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
}
