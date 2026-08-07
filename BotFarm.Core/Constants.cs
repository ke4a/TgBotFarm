namespace BotFarm.Core;

/// <summary>
/// Shared command and callback constants used by bots built on BotFarm.
/// </summary>
public class Constants
{
    public const string DefaultLanguage = "en-US";

    public struct Commands
    {
        public const string Start = "/start";
        public const string ChangeLanguage = "/changelanguage";
    }

    public struct Callbacks
    {
        public const string LanguageSet = "language-set";
    }

    /// <summary>
    /// Well-known "WebHookUrl" configuration values recognized by the built-in
    /// <see cref="Abstractions.IWebhookUrlResolver"/> implementations for local development tunnels.
    /// </summary>
    public struct WebhookProviders
    {
        /// <summary>Visual Studio Dev Tunnels. See <see cref="Services.WebhookUrlResolvers.DevTunnelWebhookUrlResolver"/>.</summary>
        public const string DevTunnel = "devtunnel";

        /// <summary>LocalTunnel docker-compose service. See <see cref="Services.WebhookUrlResolvers.LocalTunnelWebhookUrlResolver"/>.</summary>
        public const string LocalTunnel = "localtunnel";

        /// <summary>ngrok docker-compose service. See <see cref="Services.WebhookUrlResolvers.NgrokWebhookUrlResolver"/>.</summary>
        public const string Ngrok = "ngrok";
    }
}
