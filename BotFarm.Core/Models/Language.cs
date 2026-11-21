namespace BotFarm.Core.Models;

/// <summary>
/// Represents one localized resource file and its key/value mapping.
/// </summary>
public class Language
{
    public required string Locale { get; set; }

    public required Dictionary<string, string> Mapping { get; set; }
}
