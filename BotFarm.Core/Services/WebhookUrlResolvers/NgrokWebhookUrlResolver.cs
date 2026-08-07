using System.Net.Http.Json;
using System.Text.Json.Serialization;
using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Services.WebhookUrlResolvers;

/// <summary>
/// Resolves the webhook base URL when running docker-compose with the ngrok service.
/// Ngrok's public URL isn't known ahead of time, so it's queried from ngrok's
/// local inspection API, which is reachable from other containers on the same docker network.
/// https://ngrok.com/docs/agent/#local-web-inspection-interface
/// </summary>
public class NgrokWebhookUrlResolver(
    IHttpClientFactory httpClientFactory,
    int maxAttempts = 10,
    TimeSpan? retryDelay = null) : IWebhookUrlResolver
{
    private readonly TimeSpan _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);

    public bool CanResolve(string webHookUrl) => webHookUrl == Constants.WebhookProviders.Ngrok;

    public async Task<string> Resolve(string webHookUrl, CancellationToken cancellationToken = default)
    {
        var ngrokApiUrl = Environment.GetEnvironmentVariable("NGROK_API_URL") ?? "http://ngrok:4040/api/tunnels";
        using var httpClient = httpClientFactory.CreateClient();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await httpClient.GetFromJsonAsync<NgrokTunnelsResponse>(ngrokApiUrl, cancellationToken);
                var publicUrl = response?.Tunnels?.FirstOrDefault(t => t.PublicUrl?.StartsWith("https://") == true)?.PublicUrl
                                 ?? response?.Tunnels?.FirstOrDefault()?.PublicUrl;

                if (!string.IsNullOrWhiteSpace(publicUrl))
                {
                    return publicUrl.TrimEnd('/');
                }
            }
            catch (HttpRequestException)
            {
                // ngrok container may still be starting up; retry below.
            }

            await Task.Delay(_retryDelay, cancellationToken);
        }

        throw new InvalidOperationException("Could not get ngrok tunnel URL. Ensure the ngrok container is running and NGROK_AUTHTOKEN is set.");
    }

    private sealed class NgrokTunnelsResponse
    {
        [JsonPropertyName("tunnels")]
        public List<NgrokTunnel>? Tunnels { get; set; }
    }

    private sealed class NgrokTunnel
    {
        [JsonPropertyName("public_url")]
        public string? PublicUrl { get; set; }
    }
}
