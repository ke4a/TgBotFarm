using System.Globalization;
using BotFarm.Core.Abstractions;

namespace BotFarm.Core.Localization;

/// <summary>
/// Lists the admin UI languages: the cultures that have a file in the <c>BotFarm.Shared</c> translation set,
/// each named by that file's <c>LanguageName</c> value.
/// </summary>
internal sealed class AdminLanguageCatalog : IAdminLanguageCatalog
{
    /// <summary>
    /// The translation set whose files define the supported languages.
    /// </summary>
    public const string SharedSet = "BotFarm.Shared";

    /// <summary>
    /// The key holding each language's name, written in that language.
    /// </summary>
    public const string LanguageNameKey = "LanguageName";

    /// <summary>
    /// Builds the language list from <paramref name="factory"/>'s shared set.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The shared set has no <c>en-US</c> file, a file name is not a culture name, or a file lacks <c>LanguageName</c>.
    /// </exception>
    public AdminLanguageCatalog(JsonAdminStringLocalizerFactory factory)
    {
        var cultures = factory.GetCultures(SharedSet);
        if (!cultures.Contains(JsonAdminStringLocalizerFactory.FallbackCulture, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The '{SharedSet}' admin translation set has no '{JsonAdminStringLocalizerFactory.FallbackCulture}.json' file.");
        }

        Languages = cultures
            .Select(culture => CreateLanguage(factory, culture))
            .OrderBy(language => language.Culture.Name == JsonAdminStringLocalizerFactory.FallbackCulture ? 0 : 1)
            .ThenBy(language => language.Culture.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Default = Languages[0];
    }

    /// <inheritdoc />
    public IReadOnlyList<AdminLanguage> Languages { get; }

    /// <inheritdoc />
    public AdminLanguage Default { get; }

    /// <inheritdoc />
    public AdminLanguage? Find(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return null;
        }

        return Languages.FirstOrDefault(language =>
            string.Equals(language.Culture.Name, culture, StringComparison.OrdinalIgnoreCase));
    }

    private static AdminLanguage CreateLanguage(JsonAdminStringLocalizerFactory factory, string cultureName)
    {
        CultureInfo culture;
        try
        {
            culture = CultureInfo.GetCultureInfo(cultureName, predefinedOnly: true);
        }
        catch (CultureNotFoundException exception)
        {
            throw new InvalidOperationException(
                $"The '{SharedSet}' admin translation file '{cultureName}.json' is not named after a known culture. " +
                "Check that the file name is a valid culture name, and that the app is not running in " +
                "globalization-invariant mode (DOTNET_SYSTEM_GLOBALIZATION_INVARIANT or InvariantGlobalization), " +
                "which needs ICU installed to be turned off.",
                exception);
        }

        var displayName = factory.GetValue(SharedSet, cultureName, LanguageNameKey);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException(
                $"The '{SharedSet}' admin translation file '{cultureName}.json' has no '{LanguageNameKey}' value.");
        }

        return new AdminLanguage(culture, displayName);
    }
}
