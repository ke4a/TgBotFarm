using System.Globalization;

namespace BotFarm.Core.Abstractions;

/// <summary>
/// Supported admin UI languages, taken from the shared admin translation files.
/// </summary>
public interface IAdminLanguageCatalog
{
    /// <summary>
    /// Supported languages, en-US first.
    /// </summary>
    IReadOnlyList<AdminLanguage> Languages { get; }

    /// <summary>
    /// The default and fallback language (en-US).
    /// </summary>
    AdminLanguage Default { get; }

    /// <summary>
    /// Returns the supported language whose culture name equals <paramref name="culture"/> ignoring case, or null.
    /// </summary>
    AdminLanguage? Find(string? culture);
}

/// <summary>
/// An admin UI language: its culture and its name written in that language.
/// </summary>
/// <param name="Culture">The specific culture the admin UI runs in, e.g. <c>uk-UA</c>.</param>
/// <param name="DisplayName">The language's name in that language, e.g. <c>Українська</c>.</param>
public sealed record AdminLanguage(CultureInfo Culture, string DisplayName);
