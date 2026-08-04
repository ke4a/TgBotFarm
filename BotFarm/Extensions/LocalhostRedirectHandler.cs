namespace BotFarm.Extensions;

/// <summary>
/// Rewrites wildcard health-check callback hosts to <c>localhost</c> so self-calls succeed locally.
/// </summary>
public class LocalhostRedirectHandler : DelegatingHandler
{
    /// <summary>
    /// Replaces wildcard hosts with loopback before delegating the request.
    /// </summary>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null)
        {
            return base.SendAsync(request, cancellationToken);
        }

        // If URI resolved against 0.0.0.0 or [::], or any other wildcard - then rewrite to loopback, keep scheme+port
        if (uri.Host is "0.0.0.0" or "[::]" or "::" or "*" or "+")
        {
            request.RequestUri = new UriBuilder(uri) { Host = "localhost" }.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
