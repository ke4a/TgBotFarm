---
type: architecture
title: Bot Settings
description: How each bot's token, admin chat, webhook secret and emoji are stored encrypted in MongoDB, loaded into named BotConfig options, validated and verified on save, applied to a running bot without a restart, and edited from the dashboard.
tags: [architecture, bots, settings, security, persistence]
verified:
  - by: openwiki/0.7.0
    at: 2026-11-25T16:31:11.118Z
sources:
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-84552c233d3d33b8cd394c16
    resource: repo://BotFarm.Core/Models/BotSettings.cs
  - id: openwiki-source-4f41a333f2932b76e7b632eb
    resource: repo://BotFarm.Core/Models/BotSettingsView.cs
  - id: openwiki-source-554d6aed4058b870daf58f39
    resource: repo://BotFarm.Core/Services/AppDataPaths.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-a202dffcd329097f36bcfbd4
    resource: repo://BotFarm.Core/Services/BotSettingsOptions.cs
  - id: openwiki-source-721b7f5104582a12970cd53e
    resource: repo://BotFarm.Core/Services/BotSettingsProtector.cs
  - id: openwiki-source-cf8c4beffd2134d6e08d23b5
    resource: repo://BotFarm.Core/Services/BotSettingsProvider.cs
  - id: openwiki-source-fd8329a660d312e225645b1a
    resource: repo://BotFarm.Core/Services/MongoBotSettingsStore.cs
  - id: openwiki-source-23d8d972a87f9e0f29281045
    resource: repo://BotFarm.Core/Services/TelegramTokenVerifier.cs
  - id: openwiki-source-358534168ec187b0e4e08640
    resource: repo://BotFarm.Core/Telemetry/BotEventMetrics.cs
  - id: openwiki-source-ade696e5c56b5adbd333dd6e
    resource: repo://BotFarm.Shared/Components/BotSettingsDialog.razor.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
generated: { by: "claude-code", at: "2026-11-25T16:31:11.118Z" }
---

# Bot Settings

Each registered bot's Telegram token, admin chat ID, optional custom webhook secret and optional emoji are administrator-managed data, not configuration. They live in MongoDB, are edited on the bot's dashboard page, and take effect without a restart. Configuration (`Bots:<name>:BotConfig`) is no longer read, and there is no `Enabled` flag: a bot's on/off state is the separate durable desired state described in [Bot Runtime Lifecycle](./bot-runtime.md).

## Components

| Piece | Role |
| --- | --- |
| `BotSettings` | Stored document, keyed by bot name: `ProtectedToken`, `TelegramBotId`, `AdminChatId`, `ProtectedWebhookSecret`, `Emoji`, `UpdatedAtUtc`, `UpdatedBy`. |
| `MongoBotSettingsStore` (`IBotSettingsStore`) | Reads, upserts and looks up the `BotSettings` collection in the `BotFarmControl` database. |
| `BotSettingsProtector` | Encrypts and decrypts secrets with ASP.NET Core Data Protection. |
| `BotSettingsProvider` (`IBotSettingsProvider`) | In-memory cache of each bot's loaded document and decrypted secrets; persists accepted changes. |
| `BotSettingsOptionsSetup` / `BotSettingsChangeTokenSource` | Fill each bot's named `BotConfig` from the provider and tell `IOptionsMonitor` when it changed. |
| `BotControlCoordinator` (`IBotSettingsService`) | Validates, verifies and saves changes and brings the running bot in line. |
| `TelegramTokenVerifier` (`ITelegramTokenVerifier`) | Calls `getMe` to check a new token. |
| `DashboardBotSettings`, `BotSettingsDialog` | Dashboard card and edit dialog in `BotFarm.Shared`. |

`AddCoreServices` registers all of these as singletons; the coordinator is exposed as both `IBotControlService` and `IBotSettingsService`. A bot module calls `AddBotConfig(name)`, which adds the named options and a change-token source for that bot; it does not bind configuration. See [Adding a Bot Module](../workflows/adding-a-bot.md).

## Storage and encryption

Documents are stored in `BotFarmControl.BotSettings`, next to `BotControlStates` (see [Persistence and Backups](./persistence-and-backups.md)). On first use the store creates a sparse unique index on `TelegramBotId`, so no two bots can store the same Telegram bot while any number of bots store no token; `TelegramBotId` is omitted from the document when absent so the sparse index skips it.

The token and custom webhook secret are stored only in protected form. `BotSettingsProtector` creates a protector with purpose `BotFarm.BotSettings.v1` plus the bot name, so a value protected for one bot cannot be decrypted for another. The host persists the key ring to the `dpkeys` folder under the app data folder with application name `BotFarm`; losing or replacing that folder makes the stored secrets unreadable (see [Configuration and Operations](../operations/deployment.md)). The admin chat, emoji and Telegram bot ID are stored in plain form. The settings database is not part of per-bot backups.

## Loading and settings states

`BotSettingsProvider.LoadAsync` reads a bot's document and decrypts its secrets, replacing the cached entry. Its result is a `BotSettingsState`:

- `Unknown` - the store could not be read. The previously loaded entry is left in place, so a bot that was working keeps its client and admin alerts through a store outage.
- `Incomplete` - no document, or the token or a non-zero admin chat is missing.
- `Unreadable` - a stored secret could not be decrypted (`CryptographicException`: other key ring, other bot's purpose, altered data). The secret is left out, its name (`Token`, `WebhookSecret`) is listed in the view's `UnreadableSettings`, and one error naming the settings, never their values, is logged per load.
- `Configured` - token and admin chat present and every stored secret readable.

Each load or persist fires the bot's change token, so `IOptionsMonitor<BotConfig>` re-reads it: `Configure` copies the decrypted token (empty when unusable), admin chat (zero when missing), webhook secret and emoji into the bot's `BotConfig`. The coordinator then calls `IBotService.ReloadClient()` so the next Telegram call uses the current token.

The coordinator loads settings at startup and on Retry. A bot whose settings are not `Configured` becomes `NotConfigured`: it is gated, cannot be turned on, and its webhook answers 503 (see [Telegram Webhook Integration](../integrations/telegram-webhooks.md)).

## Read model

`IBotSettingsService.GetView` returns a `BotSettingsView` that never contains a token or secret: state, whether a token is stored, the Telegram bot ID, admin chat, emoji, whether a custom webhook secret is stored, last update time and administrator, and the unreadable setting names. The coordinator adds the Telegram username only when the running bot's `Me` reports the stored Telegram bot ID.

## Saving a change

`SaveAsync(botName, BotSettingsChange)` runs under the bot's operation lock, the same lock used by enable, disable, retry and maintenance pauses; if the lock is held it returns `Rejected(BotBusy)` at once. A change carries an optional new token, optional admin chat, optional new webhook secret or `UseDerivedWebhookSecret`, the emoji (empty clears it), `ConfirmAccountChange`, and the administrator's user name. Empty token or secret inputs keep the stored values; submitted token and secret are trimmed.

The steps, in order:

1. If the bot's settings were never loaded (`Unknown`), load them again; if the store is still unreadable, reject with `StoreUnavailable`. A save never writes over settings that are not loaded.
2. Validate formats, collecting one `BotSettingsRejection` per problem: `InvalidToken` (not `<digits>:<non-space>`, or missing while the stored token is unreadable), `InvalidAdminChat` (missing or zero), `InvalidWebhookSecret` (not 1-256 of `A-Z a-z 0-9 _ -`, combined with the derived option, or missing while the stored secret is unreadable), `InvalidEmoji` (more than one visible character or 16 UTF-16 units).
3. A token or webhook secret change is rejected as `BotBusy` while the bot is `Unknown` or `Pending`, because its on/off state is not known.
4. A new token is verified with Telegram: 401/404 gives `RejectedByTelegram`, any other failure `TelegramUnreachable`. Then the store is checked for another bot with the same Telegram bot ID (`DuplicateAccount`). If the token belongs to a different Telegram bot than the stored one and the change is not confirmed, the result is `NeedsAccountChangeConfirmation` with both IDs and nothing is written.
5. A running, enabled bot whose token or secret changes is gated as `Pending` before the write, because the new secret is checked on incoming updates as soon as it is stored while Telegram still sends the old one. If the write fails, the previous status is restored.
6. `PersistAsync` protects new secrets, keeps stored ones otherwise, and upserts the document. A duplicate-key write error becomes `DuplicateAccount`; any other persistence failure `StoreUnavailable`.
7. The bot is brought in line:
   - `NotConfigured` bot: reload the client; if the settings are now `Configured`, apply its saved (or newly seeded, disabled) desired state.
   - Only admin chat or emoji changed: nothing to apply; they are read on next use.
   - Token changed on an enabled bot: delete the webhook with the old client (failures ignored, since a revoked token cannot do it), reload the client, re-apply enabled. On a disabled bot only the client is reloaded.
   - Webhook secret changed on an enabled bot: re-apply enabled.
   - Bot in `Error`: re-apply its desired on/off state with the new settings, so a bot that failed because of its token can be repaired.

   If applying fails, the save is still reported as `Saved` and the bot is left in `Error` with a sanitized message; Retry re-applies it.

`Saved` carries the new view, the runtime status, and the username Telegram reported for a newly verified token. Only `Saved` changes stored data.

## Audit logging and metrics

An accepted save logs information naming the bot, the administrator and the changed setting names; a rejection logs a warning with the rejection categories and submitted setting names; a pending account change logs both Telegram bot IDs. Values are never logged. A save that changed at least one setting records a `settings_changed` bot event (see [Metrics and Observability](../operations/observability.md)).

## Dashboard

`DashboardBotSettings` is a card on each bot's dashboard page. It shows the view without secrets and a warning icon when the bot is not configured (amber when incomplete, red when unknown or unreadable), and opens `BotSettingsDialog`. When the dialog saves, the card raises `OnSaved` so the page reloads the bot's status.

`BotSettingsDialog` prefills the non-secret values. The token and webhook secret inputs are write-only plain text inputs with autocomplete and spellcheck turned off; they start empty and are cleared after a save. The emoji is chosen with the vendored `emoji-picker-element` or cleared, never typed. When a secret is unreadable the dialog requires a replacement before sending. A `NeedsAccountChangeConfirmation` result asks the administrator to confirm and resubmits with `ConfirmAccountChange`. See [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Focused tests

- `BotSettingsProviderTests`, `BotSettingsProtectorTests` - states, unreadable secrets, change tokens, per-bot protection.
- `BotControlCoordinatorSettingsTests`, `BotSettingsAuditLogTests` - validation, verification, duplicate and account-change handling, gating and re-apply, store failures, log content.
- `TelegramTokenVerifierTests`, `TelegramClientExceptionTests` - verification outcomes and that client exceptions never contain the token.
- Integration: `MongoBotSettingsStoreTests`, `BotSettingsEncryptionTests`, `BotSettingsStartupTests`, `BotSettingsSecretExposureTests`.
- UI: `DashboardBotSettingsTests`, `BotSettingsDialogTests`.

The feature's requirements are in `specs/005-admin-bot-settings/`.
