namespace BotFarm.Localization;

/// <summary>
/// Adds admin UI localization to the request pipeline.
/// </summary>
public static class AdminLocalizationApplicationExtensions
{
    /// <summary>
    /// Runs request localization and <see cref="CultureCookieMiddleware"/> for admin UI requests only, as decided by
    /// <see cref="AdminLocalizationScope.Applies"/>. Other requests keep the server's default culture and never
    /// receive the language cookie.
    /// </summary>
    public static IApplicationBuilder UseAdminLocalization(this IApplicationBuilder app) =>
        app.UseWhen(
            context => AdminLocalizationScope.Applies(context.Request.Path),
            branch => branch
                .UseRequestLocalization()
                .UseMiddleware<CultureCookieMiddleware>());
}
