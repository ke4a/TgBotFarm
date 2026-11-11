using BotFarm.Core.Abstractions;

namespace BotFarm.Localization;

/// <summary>
/// Maps the endpoint that switches the admin UI language.
/// </summary>
public static class AdminLanguageEndpoints
{
    /// <summary>
    /// The path of the language switch endpoint.
    /// </summary>
    public const string SetCulturePath = "/culture/set";

    /// <summary>
    /// Maps anonymous <c>GET /culture/set?culture=&lt;name&gt;&amp;returnUrl=&lt;local url&gt;</c>. A supported
    /// <c>culture</c>, matched ignoring case, is stored in the language cookie; any other value is ignored. The
    /// response always redirects: to <c>returnUrl</c> when it is a local URL, and to <c>/</c> otherwise.
    /// </summary>
    public static IEndpointRouteBuilder MapAdminLanguageEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(SetCulturePath, SetCulture).AllowAnonymous();
        return endpoints;
    }

    private static IResult SetCulture(
        HttpContext context,
        IAdminLanguageCatalog catalog,
        string? culture,
        string? returnUrl)
    {
        if (catalog.Find(culture) is { } language)
        {
            CultureCookie.Append(context.Response, context.Request, language.Culture);
        }

        return Results.Redirect(IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }

    /// <summary>
    /// Applies the rules of <c>IUrlHelper.IsLocalUrl</c>: the URL starts with <c>/</c> but not <c>//</c> or
    /// <c>/\</c>, or starts with <c>~/</c> but not <c>~//</c> or <c>~/\</c>, and holds no control characters.
    /// </summary>
    internal static bool IsLocalUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return false;
        }

        int prefixLength;
        if (url[0] == '/')
        {
            prefixLength = 1;
        }
        else if (url.Length > 1 && url[0] == '~' && url[1] == '/')
        {
            prefixLength = 2;
        }
        else
        {
            return false;
        }

        var path = url.AsSpan(prefixLength);
        if (path.Length > 0 && path[0] is '/' or '\\')
        {
            return false;
        }

        foreach (var character in path)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }
}
