using BotFarm.Core.Abstractions;
using Microsoft.Extensions.Configuration;

namespace BotFarm.Core.Services;

/// <summary>
/// Resolves the runtime data folders from the <c>DataPath</c> setting.
/// </summary>
/// <remarks>
/// When <c>DataPath</c> is empty, the app folder is used. A relative <c>DataPath</c> is resolved against the app folder.
/// Folders are not created here; each consumer creates its folder when it first needs it.
/// </remarks>
public sealed class AppDataPaths : IAppDataPaths
{
    /// <summary>
    /// Name of the configuration setting (and environment variable) that sets the data folder.
    /// </summary>
    public const string ConfigurationKey = "DataPath";

    /// <summary>
    /// Creates the data folders from the <c>DataPath</c> setting in <paramref name="configuration"/>.
    /// </summary>
    public AppDataPaths(IConfiguration configuration)
        : this(ResolveRoot(configuration[ConfigurationKey]))
    {
    }

    private AppDataPaths(string root)
    {
        Root = root;
        Backups = Path.Combine(root, "backups");
        Logs = Path.Combine(root, "logs");
        Temp = Path.Combine(root, "tmp");
        DataProtectionKeys = Path.Combine(root, "dpkeys");
    }

    /// <inheritdoc />
    public string Root { get; }

    /// <inheritdoc />
    public string Backups { get; }

    /// <inheritdoc />
    public string Logs { get; }

    /// <inheritdoc />
    public string Temp { get; }

    /// <inheritdoc />
    public string DataProtectionKeys { get; }

    /// <summary>
    /// Turns a configured data path into a full path, using the app folder when the value is empty.
    /// </summary>
    public static string ResolveRoot(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return AppContext.BaseDirectory;
        }

        return Path.GetFullPath(configuredPath, AppContext.BaseDirectory);
    }
}
