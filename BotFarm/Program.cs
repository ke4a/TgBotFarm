using BotFarm.Extensions;
using NLog.Web;

namespace BotFarm;

public class Program
{
    public static DateTime StartTime { get; private set; }

    public static async Task Main(string[] args)
    {
        StartTime = DateTime.UtcNow;

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseStaticWebAssets();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Host.UseNLog();
        builder.Services.Configure<HostOptions>(options => { options.ShutdownTimeout = TimeSpan.FromSeconds(25); });
        builder.AddBotFarm();

        var app = builder.Build();
        app.UseBotFarm();
        await app.RunAsync();
    }
}
