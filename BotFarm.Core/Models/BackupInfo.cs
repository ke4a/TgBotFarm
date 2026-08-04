namespace BotFarm.Core.Models;

/// <summary>
/// Describes a backup archive available on disk.
/// </summary>
public class BackupInfo
{
    public string Name { get; set; }

    public long Size { get; set; }

    public DateTime Date { get; set; }
}
