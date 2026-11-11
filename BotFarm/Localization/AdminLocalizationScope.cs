namespace BotFarm.Localization;

/// <summary>
/// Decides which requests belong to the admin UI and therefore take their culture from the operator's language.
/// </summary>
/// <remarks>
/// Bot webhooks and the dashboard API live under <c>/api</c>, and health endpoints under <c>/health</c> and
/// <c>/healthz</c>. These run in the server's default culture so culture-sensitive bot code behaves the same
/// whatever the caller sends.
/// </remarks>
public static class AdminLocalizationScope
{
    private static readonly PathString[] ExcludedSegments = ["/api", "/health", "/healthz"];

    /// <summary>
    /// Returns false for paths under the <c>/api</c>, <c>/health</c> or <c>/healthz</c> segments, compared ignoring
    /// case, and true for every other path.
    /// </summary>
    public static bool Applies(PathString path) =>
        !ExcludedSegments.Any(segment => path.StartsWithSegments(segment, StringComparison.OrdinalIgnoreCase));
}
