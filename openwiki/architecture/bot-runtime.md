---
type: architecture
title: Bot Runtime Lifecycle
description: Explains how BotFarm restores and persists each bot's desired state, applies it to Telegram webhooks, pauses bots for maintenance and shutdown, gates incoming updates, and routes updates to handlers.
tags: [architecture, bots, lifecycle, updates]
sources:
  - id: openwiki-source-6f7dea2a166b86ef9000b099
    resource: repo://BotFarm.Core/Base/BotService.cs
  - id: openwiki-source-60145f991035e51f8a6872fc
    resource: repo://BotFarm.Core/Base/UpdateService.cs
  - id: openwiki-source-97e8c28339d027a055eb5bf6
    resource: repo://BotFarm.Core/Controllers/UpdateControllerBase.cs
  - id: openwiki-source-fcf0387dcbb793f844dc028a
    resource: repo://BotFarm.Core/Helpers/TgUserAuthorizationHelper.cs
  - id: openwiki-source-7e0fbf7825e41e66fdf4eae2
    resource: repo://BotFarm.Core/Models/BotControlState.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-32d040660ba266e85195778b
    resource: repo://BotFarm.Core/Services/JsonLocalizationService.cs
  - id: openwiki-source-a06f6c23ee056cc162bc8d5a
    resource: repo://BotFarm.Core/Services/MongoBotControlStateStore.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-e5ee530d12cdb4cc0ab3552a
    resource: repo://BotFarm/HostedServices/BotControlStartupService.cs
  - id: openwiki-source-254c9b517d5aa3409beaff97
    resource: repo://TestBot/Controllers/UpdateController.cs
  - id: openwiki-source-b273e7de5ce8bb6689e05057
    resource: repo://TestBot/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-499bbbc570f0855093041b33
    resource: repo://TestBot/Handlers/Callbacks/ClearChatDataCallbackHandler.cs
  - id: openwiki-source-95cab4955d797ab14333afb9
    resource: repo://TestBot/Handlers/Commands/ClearChatDataCommandHandler.cs
  - id: openwiki-source-164f87ae52eb9af8790c8a85
    resource: repo://TestBot/Services/TestBotUpdateService.cs
  - id: openwiki-source-84fd40ef616413cb117d41e1
    resource: repo://tests/BotFarm.Core.UnitTests/Helpers/TgUserAuthorizationHelperTests.cs
  - id: openwiki-source-d0f413eb6f380ffa2d995247
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs
  - id: openwiki-source-1f3f0ea03c3927e809e04ee4
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotServiceTests.cs
  - id: openwiki-source-2e24eb17d0d649cf91303988
    resource: repo://tests/BotFarm.Core.UnitTests/Services/UpdateServiceTests.cs
  - id: openwiki-source-39c908a420b2fc3ea4e2c02c
    resource: repo://tests/TestBot.UnitTests/Services/TestBotUpdateServiceTests.cs
generated: { by: "claude-code", at: "2026-11-12T18:30:04.833Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T18:30:04.833Z
---

# Bot Runtime Lifecycle

## Ownership and registration

The web host owns process startup. Reusable lifecycle and update-processing behavior lives in `BotFarm.Core`, and each bot project contributes its implementation through an `IBotModule` (see [Adding a Bot Module](../workflows/adding-a-bot.md)). The host registers each bot's `BotIdentity` as a singleton keyed by the bot name; the bot's module then registers its `IBotService`, `IUpdateService`, database services, and command/callback handlers as keyed services under that same name. `TestBot` is the reference bot hosted in the same process.

`BotService` (in `BotFarm.Core.Base`) resolves the `BotConfig` named by `BotIdentity.Name`, requires a token, and creates the authenticated Telegram client. It provides initialization (`GetMe` plus a per-bot temporary folder under the data folder), webhook setup with the bot's secret token, and `Pause`, which deletes the webhook and returns `false` after logging a non-cancellation failure. It does not own the desired enabled flag and has no resume operation of its own; the control coordinator is authoritative for lifecycle state and re-applies it.

## Durable desired state and process-local status

`BotControlState` is the durable desired state, stored in the `BotFarmControl` MongoDB database's `BotControlStates` collection with the bot name as record ID. It holds the desired enabled value, the last command ID, and its UTC update time. `MongoBotControlStateStore` reads by bot name, inserts an initial state, and updates desired state atomically with find-and-update.

`BotRuntimeStatus` is an in-memory view of whether this process has applied the desired state. Its `Unknown`, `Pending`, `Applied`, and `Error` states include nullable desired/applied values, last-attempt time, and a sanitized error summary.

`BotControlStartupService` is a hosted service that waits for `ApplicationStarted`, so the web server already accepts requests when webhooks are registered, and then calls `BotControlCoordinator.InitializeAsync`. Stopping the host during initialization ends the service quietly. For each bot, the coordinator loads the persisted state; if no record exists, it seeds one from `Bots:{name}:BotConfig:Enabled`. A concurrent initial insert is handled by loading the record that won.

## Applying an operator command

The coordinator serializes operations per bot with one semaphore each. `SetEnabledAsync` is accepted only when that bot is `Applied`; if another operation holds the lock or the status is not stable, it throws `BotControlBusyException`. It marks the bot `Pending`, creates a command ID, and persists the requested target before contacting Telegram. An unconfirmed write is kept in memory so that `RetryAsync` can persist the same command ID first.

After persistence is confirmed, enabling initializes the bot, resolves the webhook base URL, and sets the bot's update endpoint; disabling succeeds only when the bot confirms the webhook was deleted. A transition gets up to three attempts, waiting one and then two seconds between them. Success records the target as both desired and applied. Exhausted failures leave `Error` with the previous applied value; persistence uncertainty or cancellation leaves `Unknown`. Status errors contain exception type names and attempt counts, never exception messages that could include secrets.

When a MongoDB write throws after an ambiguous acknowledgement, the coordinator reads the record back. A matching command ID proves the write committed; otherwise the change stays unconfirmed. `RetryAsync` is accepted only from `Unknown` or `Error`: it re-persists an in-memory unconfirmed command, or otherwise reloads the authoritative stored state and applies it.

## Maintenance and shutdown pauses

`RunWithUpdatesPausedAsync` runs an action, such as a backup or restore, while the bot's webhook is paused, holding the bot's operation lock throughout:

- An `Applied`, enabled bot is marked `Pending` and paused. If the pause is not confirmed, it throws `BotUpdatesPauseException` and the action does not run. After the action (even if it failed or was cancelled), the coordinator re-applies the enabled state with retries using a non-cancellable token. If that still ends in `Error`, it sends an admin alert asking the operator to use Retry.
- An `Applied`, disabled bot runs the action without touching the webhook.
- A bot in `Unknown`, `Pending`, or `Error` is paused if possible and stays off afterwards; the operator brings it back with Retry.

`PauseAllForShutdownAsync` pauses every bot ahead of a host shutdown and leaves each `Pending`, so updates stay gated and operator commands are rejected until the process stops. The dashboard shutdown API uses it; see [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Webhook inspection and pending updates

`GetWebhookInfoAsync` reads the bot's webhook registration and delivery health (URL, pending update count, last error) straight from Telegram; it takes no lock and changes nothing. `DropPendingUpdatesAsync` discards the updates Telegram has queued for the bot without changing whether it is enabled. It takes the bot's operation lock without waiting and throws `BotControlBusyException` if an enable, disable or maintenance pause holds it, so the webhook is never changed while a transition is being applied. The work is done by `BotService.DropPendingUpdates`: with a registered webhook it sets the same URL again with the bot's secret and `dropPendingUpdates`; without one it calls `DeleteWebhook` with `dropPendingUpdates`, so the bot stays without a webhook. The dashboard's webhook card uses both operations; see [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Inbound update gate and routing

`UpdateService.CanProcessUpdates` delegates to the coordinator: a bot may process updates only when its status is `Applied` and `AppliedEnabled` is true. The shared `UpdateControllerBase` checks the webhook secret and then this gate before forwarding the update; the gate is closed as soon as a state-changing command or maintenance pause begins. Secret validation, duplicate suppression, and failure responses are described in [Telegram Webhook Integration](../integrations/telegram-webhooks.md).

The generic `UpdateService<TChatSettings>` base class owns routing:

- A bot command is handled when it starts the message and either the chat is private or the command is addressed to this bot's username (`/start@my_bot`, case-insensitive). `/start` sends the localized welcome, `/changelanguage` sends the language chooser, and other commands go to the registered `ICommandHandler` with that key.
- Callback data is split at the first `:` into a key and a parameter. `language-set` stores the chat language only when the bot has a translation for it (compared ignoring case against `ILocalizationService.GetAvailableLanguages`); an unsupported language is logged as a warning, the callback is just acknowledged, and nothing is saved. Other keys go to the registered `ICallbackHandler`. Callbacks without a message or with empty data are ignored.
- A message announcing that this bot joined a chat sends the welcome.
- The service message Telegram posts in a new supergroup upgraded from a group (`MigrateFromChatId`) moves the chat's data from the group's ID to the supergroup's ID through `IDatabaseService.MigrateChat`, then calls the overridable `OnChatMigrated(fromChatId, toChatId)` hook. The matching notice in the old group (`MigrateToChatId`) is not treated as a migration and falls through to `OnUpdate`, so the move runs once per upgrade.
- Anything else goes to the overridable `OnUpdate`. A command or callback key with no registered handler completes without action.

Chat texts come from `ILocalizationService.GetLocalizedString`, which looks the key up in the chat's language, then in the default language, and returns an empty string only when the default language lacks the key too. A chat whose stored language has no translation file therefore still gets default-language texts.

Before moving data the router logs both chat IDs. `MigrateChat` moves the chat settings and then the bot's own per-chat data (see [Persistence and Backups](./persistence-and-backups.md)). When the move fails, the router logs an error with both chat IDs, rethrows to the caller of `ProcessUpdate`, and skips `OnChatMigrated`. The default `OnChatMigrated` does nothing; a bot overrides it only for one-time side effects such as messaging the upgraded chat, which must target the new supergroup ID because the old group no longer accepts messages.

`TestBotUpdateService` only overrides `OnUpdate`: it saves animation (GIF) messages with the chat ID, sender user ID, and file ID, skipping messages without a sender user. When saving throws, it logs the failure and sends an error notification.

TestBot's `/clearchatdata` command shows its confirmation prompt only in private chats or to a group administrator; others get a localized refusal. `TgUserAuthorizationHelper` allows private chats directly. In groups and supergroups, a message posted on behalf of the group itself (its `SenderChat` is the chat) comes from an anonymous administrator and passes without a lookup; this shortcut applies only when no separate callback sender is supplied, and a message sent as another chat (such as a linked channel) does not get it. Otherwise the helper queries Telegram's administrator list for the sender. The confirmation callback repeats the check against the callback sender before deleting data, and a non-admin gets a popup alert. Telegram API, HTTP, or timeout failures during the lookup are logged and deny the action.

## Focused tests

`BotControlCoordinatorTests` and `BotControlCoordinatorInitializationTests` cover persistence-before-apply, startup seeding, per-bot concurrency, gating, dropping pending updates under the operation lock, ambiguous writes, retries, maintenance pauses, and sanitized failures. `BotControlStartupServiceTests` cover waiting for application start and quiet cancellation. `UpdateServiceTests` and `TestBotUpdateServiceTests` cover command addressing, callback parsing, handler dispatch, the welcome flow, supergroup migration routing (data move before the hook, old-group notice ignored, failure logging and skipped hook), and GIF saving. `UpdateServiceTests` also checks that an unsupported language is not saved. Authorization-helper and clear-chat handler tests cover private chats, anonymous group administrators, administrator lookup, callback-sender checks, and denials. See [Testing Strategy](../testing/strategy.md) for the full map.
