using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text.Json;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BotFarm.Core.Localization;

/// <summary>
/// Serves admin UI strings from the JSON translation files under <c>AdminLanguages/&lt;set&gt;/&lt;culture&gt;.json</c>.
/// Every set is loaded once, when the factory is created.
/// </summary>
/// <remarks>
/// A set is named after the assembly whose components use it. <see cref="Create(Type)"/> picks the set named after
/// the type's assembly, and <see cref="Create(string, string)"/> picks the set named by its location.
/// Set and culture names are compared ignoring case; keys are compared exactly.
/// </remarks>
public sealed class JsonAdminStringLocalizerFactory : IStringLocalizerFactory
{
    /// <summary>
    /// The culture whose strings are used when the current UI culture has no value for a key.
    /// </summary>
    public const string FallbackCulture = "en-US";

    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> EmptySet =
        FrozenDictionary<string, FrozenDictionary<string, string>>.Empty;

    private readonly FrozenDictionary<string, FrozenDictionary<string, FrozenDictionary<string, string>>> sets;
    private readonly ConcurrentDictionary<(string Set, string Culture, string Key), byte> reportedMissingKeys = new();
    private readonly ILogger logger;

    /// <summary>
    /// Loads every translation set under <paramref name="rootPath"/>.
    /// </summary>
    /// <param name="loggerFactory">Creates the logger that reports missing keys.</param>
    /// <param name="rootPath">
    /// The folder that holds one sub-folder per set; <c>AdminLanguages</c> under the application base directory
    /// when null. A missing folder gives a factory with no sets.
    /// </param>
    /// <exception cref="InvalidDataException">A file is not a flat JSON object of string values.</exception>
    public JsonAdminStringLocalizerFactory(ILoggerFactory loggerFactory, string? rootPath = null)
    {
        logger = loggerFactory.CreateLogger<JsonAdminStringLocalizer>();
        sets = Load(rootPath ?? Path.Combine(AppContext.BaseDirectory, "AdminLanguages"));
    }

    /// <summary>
    /// Returns a localizer over the set named after <paramref name="resourceSource"/>'s assembly.
    /// </summary>
    public IStringLocalizer Create(Type resourceSource)
    {
        ArgumentNullException.ThrowIfNull(resourceSource);
        return CreateForSet(resourceSource.Assembly.GetName().Name ?? string.Empty);
    }

    /// <summary>
    /// Returns a localizer over the set named <paramref name="location"/>; <paramref name="baseName"/> is not used.
    /// </summary>
    public IStringLocalizer Create(string baseName, string location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return CreateForSet(location);
    }

    /// <summary>
    /// Lists the culture names that have a file in <paramref name="set"/>, or none when the set doesn't exist.
    /// </summary>
    public IReadOnlyCollection<string> GetCultures(string set) =>
        sets.TryGetValue(set, out var cultures) ? cultures.Keys : [];

    /// <summary>
    /// Returns the value of <paramref name="key"/> in the <paramref name="culture"/> file of <paramref name="set"/>,
    /// or null when that file or key doesn't exist. No fallback culture is consulted.
    /// </summary>
    public string? GetValue(string set, string culture, string key) =>
        sets.TryGetValue(set, out var cultures)
        && cultures.TryGetValue(culture, out var strings)
        && strings.TryGetValue(key, out var value)
            ? value
            : null;

    /// <summary>
    /// Logs a key that neither the current culture nor the fallback culture defines, once per set, culture and key.
    /// </summary>
    internal void ReportMissingKey(string set, string culture, string key)
    {
        if (reportedMissingKeys.TryAdd((set, culture, key), 0))
        {
            logger.LogDebug("Admin translation key '{Key}' is missing in set '{Set}' for culture '{Culture}'.",
                key, set, culture);
        }
    }

    private JsonAdminStringLocalizer CreateForSet(string set) =>
        new(this, set, sets.TryGetValue(set, out var cultures) ? cultures : EmptySet);

    private static FrozenDictionary<string, FrozenDictionary<string, FrozenDictionary<string, string>>> Load(string rootPath)
    {
        var result = new Dictionary<string, FrozenDictionary<string, FrozenDictionary<string, string>>>(
            StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootPath))
        {
            return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var setDirectory in Directory.EnumerateDirectories(rootPath))
        {
            var cultures = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(setDirectory, "*.json"))
            {
                cultures[Path.GetFileNameWithoutExtension(file)] = ReadFile(file);
            }

            result[Path.GetFileName(setDirectory)] = cultures.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    private static FrozenDictionary<string, string> ReadFile(string path)
    {
        JsonDocument document;
        try
        {
            // StreamReader detects and skips a UTF-8 BOM.
            using var reader = new StreamReader(path);
            document = JsonDocument.Parse(reader.ReadToEnd());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Admin translation file '{path}' is not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Admin translation file '{path}' must contain a JSON object.");
            }

            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        $"Admin translation file '{path}' has a non-string value for key '{property.Name}'.");
                }

                strings[property.Name] = property.Value.GetString()!;
            }

            return strings.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }
}
