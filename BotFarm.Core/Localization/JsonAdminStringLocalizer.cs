using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace BotFarm.Core.Localization;

/// <summary>
/// Looks up admin UI strings in one translation set for the UI culture current at the time of the call.
/// </summary>
/// <remarks>
/// A key resolves to the current UI culture's value, then the <see cref="JsonAdminStringLocalizerFactory.FallbackCulture"/>
/// value, then the key itself marked as not found.
/// </remarks>
internal sealed class JsonAdminStringLocalizer : IStringLocalizer
{
    private readonly JsonAdminStringLocalizerFactory factory;
    private readonly string set;
    private readonly FrozenDictionary<string, FrozenDictionary<string, string>> cultures;

    /// <summary>
    /// Binds the localizer to the strings of one set, keyed by culture name.
    /// </summary>
    public JsonAdminStringLocalizer(
        JsonAdminStringLocalizerFactory factory,
        string set,
        FrozenDictionary<string, FrozenDictionary<string, string>> cultures)
    {
        this.factory = factory;
        this.set = set;
        this.cultures = cultures;
    }

    /// <inheritdoc />
    public LocalizedString this[string name]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(name);
            var value = Find(name);
            return value is null
                ? new LocalizedString(name, name, resourceNotFound: true, searchedLocation: set)
                : new LocalizedString(name, value, resourceNotFound: false, searchedLocation: set);
        }
    }

    /// <inheritdoc />
    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var template = this[name];
            var value = string.Format(CultureInfo.CurrentCulture, template.Value, arguments);
            return new LocalizedString(name, value, template.ResourceNotFound, searchedLocation: set);
        }
    }

    /// <inheritdoc />
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var current = Strings(culture);
        foreach (var entry in current)
        {
            yield return new LocalizedString(entry.Key, entry.Value, resourceNotFound: false, searchedLocation: set);
        }

        if (!includeParentCultures || IsFallbackCulture(culture))
        {
            yield break;
        }

        foreach (var entry in Strings(JsonAdminStringLocalizerFactory.FallbackCulture))
        {
            if (!current.ContainsKey(entry.Key))
            {
                yield return new LocalizedString(entry.Key, entry.Value, resourceNotFound: false, searchedLocation: set);
            }
        }
    }

    private string? Find(string name)
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (Strings(culture).TryGetValue(name, out var value)
            || (!IsFallbackCulture(culture)
                && Strings(JsonAdminStringLocalizerFactory.FallbackCulture).TryGetValue(name, out value)))
        {
            return value;
        }

        factory.ReportMissingKey(set, culture, name);
        return null;
    }

    private FrozenDictionary<string, string> Strings(string culture) =>
        cultures.TryGetValue(culture, out var strings) ? strings : FrozenDictionary<string, string>.Empty;

    private static bool IsFallbackCulture(string culture) =>
        string.Equals(culture, JsonAdminStringLocalizerFactory.FallbackCulture, StringComparison.OrdinalIgnoreCase);
}
