namespace BotFarm.Core.Models;

internal class Translation
{
    public required string BotName { get; set; }

    public required List<Language> Languages { get; set; }
}
