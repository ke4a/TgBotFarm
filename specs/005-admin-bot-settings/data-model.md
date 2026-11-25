# Data Model: Admin-Managed Bot Settings

## Storage Boundary

Persist bot settings in a `BotSettings` collection in the `BotFarmControl` MongoDB database, next to
`BotControlStates`. Bot settings are never written to a bot's own database, so per-bot backups do not
contain them and restoring a bot's backup does not change them. Secrets are stored only in protected
form; the keys that unprotect them stay in the data folder's key ring, outside MongoDB.

## Entities

### BotSettings (stored)

One document for each registered bot whose settings have been saved.

| Field | Type | Required | Rules |
|---|---|---:|---|
| `botName` / document ID | string | Yes | Exact name of the registered bot identity. |
| `protectedToken` | string or null | No | Token protected with purposes `BotFarm.BotSettings.v1` and the bot name. Null when no valid token has been stored yet. |
| `telegramBotId` | long or null | No | Numeric part of the token before the colon. Unique across documents (sparse unique index). Null when there is no token. |
| `adminChatId` | long or null | No | Non-zero when set. Null when no valid value has been stored yet. |
| `protectedWebhookSecret` | string or null | No | Custom webhook secret protected like the token. Null means the secret derived from the token is used. |
| `emoji` | string or null | No | Shown next to the bot's name. One visible character. |
| `updatedAtUtc` | timestamp | Yes | Time of the last accepted save. |
| `updatedBy` | string | Yes | User name of the administrator who made the last accepted save. |

### BotSettingsSnapshot (in memory)

The decrypted, validated settings of one bot, held only by the settings provider and copied into the
named `BotConfig` options. It is never serialized, logged or sent to the dashboard.

| Field | Source |
|---|---|
| `Token` | Unprotected `protectedToken` |
| `AdminChatId` | `adminChatId` (0 when absent) |
| `WebhookSecret` | Unprotected `protectedWebhookSecret` |
| `Emoji` | `emoji` |

### BotConfig (options)

Keeps `Emoji`, `Token`, `AdminChatId` and `WebhookSecret`, filled from the snapshot. `Enabled` is
removed. An options value for a bot without usable settings has an empty token and a zero admin
chat.

### BotSettingsState (derived)

| State | Condition | Bot behavior |
|---|---|---|
| `Unknown` | The settings store could not be read. | Gated; dashboard shows the bot as unknown; no configuration fallback. |
| `Incomplete` | Read succeeded; no document, or the token or admin chat is missing. | Gated; cannot be enabled; settings can be saved. |
| `Unreadable` | A stored secret failed to unprotect. | Gated; cannot be enabled; the enabled control shows "Not configured" and the settings card "Token must be re-entered"; saving a new token (and, if the webhook secret is unreadable, a new or cleared webhook secret) restores it. |
| `Configured` | Token and admin chat present and every stored secret unprotected. | Continues to the bot's control-state load and apply. |

### BotRuntimeStatus (extended)

`BotRuntimeState` gains `NotConfigured`, used while the settings state is `Incomplete` or
`Unreadable`; the dashboard shows both as "Not configured". Control state is loaded for these bots
as for any other, and seeded disabled when missing, so the status carries the saved desired enabled
state, with applied enabled set to false. If the control state cannot be read, the bot is `Unknown`. `SetEnabledAsync` rejects bots in this state. When valid settings are saved, the
coordinator loads and applies the desired state as at startup.

### BotSettingsView (dashboard read model)

| Field | Notes |
|---|---|
| `BotName` | |
| `State` | `BotSettingsState` |
| `HasToken` | Whether a token is stored. |
| `TelegramBotId` | Non-secret account identification. |
| `TelegramUsername` | From the running bot's `Me` when the bot has been enabled since startup and `Me` has the stored bot ID; otherwise empty. Not stored. |
| `AdminChatId`, `Emoji` | |
| `HasCustomWebhookSecret` | Whether a custom secret is stored. |
| `UnreadableSettings` | Names of stored secrets that could not be unprotected (`Token`, `WebhookSecret`); empty unless the state is `Unreadable`. |
| `UpdatedAtUtc`, `UpdatedBy` | |

The view never contains a token or webhook secret.

### BotSettingsChange (command)

| Field | Rules |
|---|---|
| `Token` | Optional. Empty keeps the stored token. Must match `<digits>:<non-space characters>`. |
| `AdminChatId` | Required when the bot has none; non-zero. |
| `WebhookSecret` | Optional. Empty keeps the stored secret. 1-256 characters from `A-Z a-z 0-9 _ -`. |
| `UseDerivedWebhookSecret` | When true, removes the custom secret. Cannot be combined with `WebhookSecret`. |
| `Emoji` | Optional; empty clears it. One visible character (a text element, so an emoji with a skin tone, a flag or a joined sequence counts as one) of at most 16 UTF-16 code units. |
| `ConfirmAccountChange` | Must be true when the new token's bot ID differs from the stored one. |
| `ChangedBy` | Authenticated administrator user name. |

## Validation Rules

- Token, admin chat and webhook secret rules are the same as the current startup checks.
- A new token is accepted only after Telegram confirms it; its bot ID must not be stored for another
  registered bot.
- A token cannot be removed.
- Only registered bots can have settings saved.

## Loading

A successful read that finds no document leaves the bot `Incomplete`; configuration is never read.
A failed read is not a missing document: the bot stays `Unknown`.

## Change Flow

| Change | Bot applied enabled | Bot applied disabled or not configured |
|---|---|---|
| Emoji or admin chat | Stored; used on next read. | Same. |
| Token | Verified, stored; old webhook deleted (failure ignored); client recreated; enabled state re-applied with retries. | Verified, stored; client recreated. A not configured bot whose settings become complete is loaded and its saved desired state applied. |
| Webhook secret set or cleared | Stored; enabled state re-applied so Telegram receives the new secret. | Stored; sent the next time the bot is enabled. |

Changes that need a re-apply are rejected while the bot is `Unknown` or `Pending`. On a bot in
`Error`, a token or webhook secret change is stored and the bot's desired on/off state is applied
again with it (after deleting the old webhook, failure ignored, for a token change).

A save for a bot whose settings could not be read (`Unknown` settings state) first reloads them; if
the reload fails, the save is rejected with `StoreUnavailable`, so a save never writes over settings
the application has not read. A stored secret that could not be unprotected is kept as stored unless
the change replaces it (or, for the webhook secret, clears it).
