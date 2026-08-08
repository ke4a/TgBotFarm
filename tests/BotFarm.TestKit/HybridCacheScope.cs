using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.TestKit;

public sealed class HybridCacheScope : IDisposable
{
    private readonly IDisposable _serviceProviderDisposable;
    private readonly IServiceProvider _serviceProvider;

    private HybridCacheScope(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _serviceProviderDisposable = (IDisposable)serviceProvider;
        Cache = serviceProvider.GetRequiredService<HybridCache>();
    }

    public HybridCache Cache { get; }

    public static HybridCacheScope Create()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return new HybridCacheScope(services.BuildServiceProvider());
    }

    public void Dispose()
    {
        _serviceProviderDisposable.Dispose();
    }
}
