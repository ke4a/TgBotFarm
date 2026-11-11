using Microsoft.AspNetCore.Localization;

namespace BotFarm.Localization;

/// <summary>
/// Stores the culture each admin UI page was served in, so the language cookie records the first-visit choice,
/// replaces an unsupported value and stays valid for a year after the operator's last visit.
/// </summary>
public sealed class CultureCookieMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Creates the middleware in front of <paramref name="next"/>.
    /// </summary>
    public CultureCookieMiddleware(RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    /// <summary>
    /// For <c>GET</c> requests whose <c>Accept</c> header includes <c>text/html</c>, writes the language cookie
    /// with the request's resolved UI culture, then calls the next middleware. Other requests pass through
    /// unchanged.
    /// </summary>
    public Task InvokeAsync(HttpContext context)
    {
        if (IsDocumentRequest(context.Request)
            && context.Features.Get<IRequestCultureFeature>() is { } feature)
        {
            CultureCookie.Append(context.Response, context.Request, feature.RequestCulture.UICulture);
        }

        return _next(context);
    }

    private static bool IsDocumentRequest(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && request.Headers.Accept.Any(value =>
            value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
}
