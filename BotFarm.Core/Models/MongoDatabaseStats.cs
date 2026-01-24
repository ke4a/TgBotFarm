namespace BotFarm.Core.Models;

public class MongoDatabaseStats
{
    public string DatabaseName { get; set; } = string.Empty;

    public long Collections { get; set; }

    public double StorageSize { get; set; }

    public long Indexes { get; set; }

    public double IndexSize { get; set; }

    public double TotalSize { get; set; }

    public double Ok { get; set; }
}
