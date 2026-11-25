using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace BotFarm.IntegrationTests.Support;

/// <summary>
/// One request the <see cref="FakeOtlpEndpoint"/> received.
/// </summary>
public sealed record ReceivedRequest(
    string Path,
    string? ContentType,
    IReadOnlyDictionary<string, string> Headers,
    byte[] Body);

/// <summary>
/// A Kestrel listener on a free local port that records every request and answers with a fixed status code.
/// </summary>
public sealed class FakeOtlpEndpoint : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<ReceivedRequest> _requests = new();

    private FakeOtlpEndpoint(WebApplication app)
    {
        _app = app;
    }

    /// <summary>
    /// The base URL the app exports to, without a signal path.
    /// </summary>
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>
    /// The requests received so far, in order.
    /// </summary>
    public IReadOnlyList<ReceivedRequest> Requests => _requests.ToArray();

    public static async Task<FakeOtlpEndpoint> StartAsync(HttpStatusCode status)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var endpoint = new FakeOtlpEndpoint(app);

        app.Run(async context =>
        {
            using var body = new MemoryStream();
            await context.Request.Body.CopyToAsync(body);
            endpoint._requests.Enqueue(new ReceivedRequest(
                context.Request.Path,
                context.Request.ContentType,
                context.Request.Headers.ToDictionary(header => header.Key, header => header.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase),
                body.ToArray()));
            context.Response.StatusCode = (int)status;
        });

        await app.StartAsync();
        endpoint.BaseUrl = app.Services.GetRequiredService<IServer>().Features
            .GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();

        return endpoint;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
