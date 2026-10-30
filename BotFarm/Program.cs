using Microsoft.Extensions.Hosting;
using NLog.Web;

namespace BotFarm;

public class Program
{
    public static DateTime StartTime { get; private set; }

    public static async Task Main(string[] args)
    {
        StartTime = DateTime.UtcNow;
        await CreateHostBuilder(args).Build().RunAsync();
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