using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace BotFarm.Localization;

/// <summary>
/// Writes the admin UI language cookie read by <see cref="CookieRequestCultureProvider"/>.
/// </summary>
public static class CultureCookie
{
    /// <summary>
    /// How long the browser keeps the cookie.
    /// </summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(365);

    /// <summary>
    /// Appends <c>.AspNetCore.Culture=c=&lt;culture&gt;|uic=&lt;culture&gt;</c> to <paramref name="response"/>.
    /// The cookie lasts one year, covers the whole site, is hidden from scripts, is sent on top-level navigations
    /// from other sites, and is marked secure when <paramref name="request"/> came over HTTPS.
    /// </summary>
    public static void Append(HttpResponse response, HttpRequest request, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(culture);

        response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                MaxAge = MaxAge,
                Path = "/",
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = request.IsHttps,
                IsEssential = true,
            });
    }
}
