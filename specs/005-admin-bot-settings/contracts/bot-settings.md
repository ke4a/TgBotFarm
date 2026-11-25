# Bot Settings Interaction Contract

## Settings Service

`IBotSettingsService` (in `BotFarm.Core.Abstractions`) is the only way to read the settings view and
save settings. Every call is limited to registered bots.

| Operation | Result |
|---|---|
| `GetView(botName)` | The bot's [`BotSettingsView`](../data-model.md#botsettingsview-dashboard-read-model). Never contains a secret. |
| `SaveAsync(botName, change, cancellationToken)` | `BotSettingsSaveResult`: `Saved` with the updated view, the bot's runtime status and, when a token was verified, the username Telegram reported, `NeedsAccountChangeConfirmation` with the stored and new bot IDs, or `Rejected` with one category per problem. |

Rejection categories:

| Category | Meaning | Stored settings |
|---|---|---|
| `InvalidToken`, `InvalidAdminChat`, `InvalidWebhookSecret`, `InvalidEmoji` | A value breaks the format rules; the setting is named. | Unchanged |
| `RejectedByTelegram` | Telegram did not accept the token. | Unchanged |
| `TelegramUnreachable` | The token could not be verified. | Unchanged |
| `DuplicateAccount` | Another registered bot stores the same Telegram bot ID. | Unchanged |
| `BotBusy` | The change needs a re-apply and the bot is unknown or pending, or another operation holds the bot. | Unchanged |
| `StoreUnavailable` | The write could not be confirmed. | Unchanged as far as the application can tell; the dashboard reloads the view. |

A `Saved` result whose runtime status is `Error` means the settings are stored but re-applying the
bot failed; the existing Retry action applies.

## Dashboard

- Each bot's dashboard page has a settings card visible to the existing authenticated dashboard
  users. It shows the [`BotSettingsView`](../data-model.md#botsettingsview-dashboard-read-model)
  fields. When the settings are not configured or could not be read, a warning icon next to the
  card title names the problem; a configured bot shows none.
- The edit dialog has token and webhook secret text inputs that are always empty when opened and
  that browsers and password managers are told not to fill, correct or save;
  an empty input keeps the stored value. A separate action switches to the derived webhook secret.
- When the result is `NeedsAccountChangeConfirmation`, the dialog shows both bot IDs and saves again
  with `ConfirmAccountChange` only after the administrator confirms.
- The emoji is chosen from an emoji picker or cleared; it is never typed.
- Rejections are shown as localized messages naming the setting; no secret value is echoed back.
- Failures of Telegram calls started from the dashboard (loading chats, broadcasts, sending a
  message) are shown as localized messages with at most the Telegram error code, never the
  exception text.
- The bot enabled control shows "Not configured" for every bot in the `NotConfigured` runtime state
  and is disabled, with an "Open settings" button that opens the settings dialog. The settings card shows "Token must be
  re-entered" when the settings state is `Unreadable`.
- Operations that call Telegram through a bot (chats, webhook info, dropping pending updates,
  sending a message, broadcasts) throw `BotNotConfiguredException` for a bot without usable
  settings, and the dashboard shows a "not configured" message for it.

## Incoming Update Endpoint

A bot in the `NotConfigured` runtime state is gated like a disabled bot: HTTP 503 without invoking
bot-specific processing.

## Logs and Alerts

- Accepted change: information log with bot name, changed setting names and the administrator.
  When at least one setting changed, the `settings_changed` event is also counted on
  `botfarm.bot.events` with only the bot name and event as tags, and the monitoring dashboard marks
  it with a "Settings changed" annotation.
- Rejected change: warning log with bot name, setting names, category and the administrator.
- Unreadable secrets: one error log per bot naming the setting.
- Telegram call failures in dashboard components: error log with the bot name, the Telegram error
  code and the exception.
- Failures carry the full exception. No log, alert, health report, telemetry attribute or exception
  message contains a token or webhook secret; Telegram client exceptions never contain the token,
  and Telegram clients do not use HTTP client request logging, which writes the request URL.
- An admin alert for a bot with no usable token or admin chat is logged as a warning instead of sent.

## Configuration

- `Bots:{botName}:BotConfig` is never read, including `Enabled`.
- The MongoDB connection string, `WebHookUrl`, data folder and other application settings stay in
  configuration.
