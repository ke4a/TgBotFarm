using System.Globalization;
using BotFarm.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;

namespace BotFarm.Core.Localization;

/// <summary>
/// Picks the admin UI language from the browser's <c>Accept-Language</c> header: the first entry, in quality
/// order, that names a supported culture or shares its base language with one.
/// </summary>
public sealed class AcceptLanguageMatchProvider : RequestCultureProvider
{
    private readonly IAdminLanguageCatalog _catalog;

    /// <summary>
    /// Creates a provider that matches against the languages in <paramref name="catalog"/>.
    /// </summary>
    public AcceptLanguageMatchProvider(IAdminLanguageCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// Parses <c>Accept-Language</c>, orders its entries by quality (descending, header order on ties) and skips
    /// <c>*</c> and zero-quality entries. For each entry it returns the supported culture with the same name
    /// (ignoring case), else the first supported culture with the same two-letter language. Returns no result
    /// when the header is missing, malformed or matches nothing.
    /// </summary>
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var header = httpContext.Request.Headers.AcceptLanguage;
        if (header.Count == 0
            || !StringWithQualityHeaderValue.TryParseList(header, out var entries)
            || entries.Count == 0)
        {
            return NullProviderCultureResult;
        }

        // OrderByDescending is a stable sort, so entries with equal quality keep their header order.
        var candidates = entries
            .Where(entry => entry.Value.HasValue && entry.Value.Value != "*" && (entry.Quality ?? 1) > 0)
            .OrderByDescending(entry => entry.Quality ?? 1);

        foreach (var entry in candidates)
        {
            if (Match(entry.Value.Value!) is { } culture)
            {
                return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture.Name));
            }
        }

        return NullProviderCultureResult;
    }

    private CultureInfo? Match(string name)
    {
        if (_catalog.Find(name) is { } exact)
        {
            return exact.Culture;
        }

        string language;
        try
        {
            language = CultureInfo.GetCultureInfo(name).TwoLetterISOLanguageName;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }

        return _catalog.Languages
            .Select(supported => supported.Culture)
            .FirstOrDefault(supported =>
                string.Equals(supported.TwoLetterISOLanguageName, language, StringComparison.OrdinalIgnoreCase));
    }
}
