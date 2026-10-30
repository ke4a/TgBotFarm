namespace BotFarm.Core.Abstractions;

/// <summary>
/// Provides the folders where the app keeps its runtime data, such as backups, logs and temporary files.
/// </summary>
public interface IAppDataPaths
{
    /// <summary>
    /// Gets the folder that holds all runtime data folders.
    /// </summary>
    string Root { get; }

    /// <summary>
    /// Gets the folder where database backup archives are stored.
    /// </summary>
    string Backups { get; }

    /// <summary>
    /// Gets the folder where log files are written.
    /// </summary>
    string Logs { get; }

    /// <summary>
    /// Gets the folder for short-lived working files.
    /// </summary>
    string Temp { get; }

    /// <summary>
    /// Gets the folder where the keys that protect sign-in cookies are stored.
    /// </summary>
    string DataProtectionKeys { get; }
}
