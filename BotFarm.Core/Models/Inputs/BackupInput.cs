using System.Text.Json.Serialization;

namespace BotFarm.Core.Models.Inputs;

public class BackupInput
{
    [JsonPropertyName("name")]
    public string Name { get; set; }
}
