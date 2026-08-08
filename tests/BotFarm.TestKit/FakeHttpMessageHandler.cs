using System.Net;

namespace BotFarm.TestKit;

/// <summary>
/// A minimal <see cref="HttpMessageHandler"/> stub for unit-testing code that depends on
/// <see cref="HttpClient"/>/<see cref="IHttpClientFactory"/>, without making real network calls.
/// </summary>
public class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}
