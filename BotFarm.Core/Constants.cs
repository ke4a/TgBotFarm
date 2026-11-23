namespace BotFarm.Core;

/// <summary>
/// Shared command and callback constants used by bots built on BotFarm.
/// </summary>
public class Constants
{
    public const string DefaultLanguage = "en-US";

    /// <summary>
    /// Header Telegram uses to echo the secret token registered with <c>setWebhook</c>.
    /// </summary>
    public const string SecretTokenHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    /// <summary>
    /// How long Telegram waits for a webhook response before it gives up on the request.
    /// </summary>
    public static readonly TimeSpan TelegramWebhookTimeout = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// Values shared by the OpenTelemetry metrics export and the Grafana dashboard links.
    /// </summary>
    public static class Telemetry
    {
        /// <summary>Configuration section that holds the telemetry export settings.</summary>
        public const string SectionName = "Telemetry";

        /// <summary>Command key recorded for a command that is neither built in nor registered by the bot.</summary>
        public const string UnknownCommand = "(unknown)";

        /// <summary>Path appended to the OTLP/HTTP base endpoint to reach the metrics receiver.</summary>
        public const string MetricsPath = "/v1/metrics";

        /// <summary>Path appended to the OTLP/HTTP base endpoint to reach the logs receiver.</summary>
        public const string LogsPath = "/v1/logs";

        /// <summary>Id of the App memory panel on the Grafana dashboard, used to link straight to it.</summary>
        public const int MemoryPanelId = 5;
    }
}
