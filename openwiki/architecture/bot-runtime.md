---
type: architecture
title: Bot Runtime Lifecycle
description: Explains how BotFarm restores and persists each bot's desired state, applies it to Telegram webhooks, gates incoming updates, and reports recoverable transition failures.
tags: [architecture, bots, lifecycle, updates]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-06T14:43:11.351Z
sources:
  - id: openwiki-source-4726dde41bf8fa012078aa9f
    resource: repo://BotFarm.Core/Abstractions/BotService.cs
  - id: openwiki-source-48906944d9d67f8e9f27c2b4
    resource: repo://BotFarm.Core/Abstractions/UpdateService.cs
  - id: openwiki-source-7e0fbf7825e41e66fdf4eae2
    resource: repo://BotFarm.Core/Models/BotControlState.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-a06f6c23ee056cc162bc8d5a
    resource: repo://BotFarm.Core/Services/MongoBotControlStateStore.cs
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
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
  - id: openwiki-source-d0f413eb6f380ffa2d995247
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs
  - id: openwiki-source-e2012c4a08961c92b5c9c92c
    resource: repo://tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs
  - id: openwiki-source-39c908a420b2fc3ea4e2c02c
    resource: repo://tests/TestBot.UnitTests/Services/TestBotUpdateServiceTests.cs
generated: { by: "copilot", at: "2026-10-06T14:43:11.351Z" }
---

# Bot Runtime Lifecycle

## Ownership and registration

The web host owns process startup; reusable bot lifecycle and update-processing behavior lives in `BotFarm.Core`, while each bot project provides its keyed implementation. `TestBot` is the reference bot hosted in the same process. Its named `BotConfig`, identity, bot and update services, and command/callback handlers are registered under the `TestBot` key. Host-wide control services enumerate the registered `IBotService` instances.

`BotService` resolves the `BotConfig` named by `BotIdentity.Name`, requires a token, and creates the authenticated Telegram client. It provides common bot initialization, webhook setup, pause, and resume operations. It does not own the desired enabled flag; the control coordinator is authoritative for that lifecycle state.

## Durable desired state and process-local status

`BotControlState` is the durable desired state, stored in the `BotFarmControl` MongoDB database's `BotControlStates` collection. Each bot name is the record ID. The record holds the desired enabled value, the last command ID, and its UTC update time. `MongoBotControlStateStore` reads by bot name, inserts an initial state, and updates desired state atomically with a find-and-update operation.

`BotRuntimeStatus` is different: it is an in-memory view of whether this process has applied the desired state. Its `Unknown`, `Pending`, `Applied`, and `Error` states include nullable desired/applied values, last-attempt time, and a sanitized error summary. On startup, `Program.Main` starts the host and then asks `BotControlCoordinator` to initialize its registered bots. For each bot, the coordinator loads the persisted state; if no record exists, it seeds one from `Bots:{name}:BotConfig:Enabled`. A concurrent initial insert is handled by loading the already-created record.

## Applying an operator command

The coordinator serializes operations independently per bot. A control command is accepted only when that bot is currently `Applied`; if another operation is active, or the status is not stable, the service reports `BotControlBusyException`. It marks the bot `Pending`, creates a command ID, and persists the requested target before contacting Telegram. An unconfirmed write is retained in process memory so an explicit retry can use the same command ID and first establish which state was saved.

After persistence is confirmed, enabling initializes the bot, resolves the webhook base URL, and sets the bot's update endpoint. Disabling asks the bot to delete its webhook; a pause is only considered successful when the bot confirms it. A transition gets up to three attempts with increasing delays. A successful transition records the target as both desired and applied. Exhausted failures leave an `Error` status with the previous applied value, while persistence uncertainty or cancellation leaves status `Unknown`. Both cases keep update processing closed until a retry or successful initialization confirms state. Errors exposed in status contain exception types and attempt counts rather than exception messages that could include secrets.

When a MongoDB write throws after an ambiguous acknowledgement, the coordinator reads the record back. A matching command ID proves that the write committed; otherwise the state change remains unconfirmed. `RetryAsync` retries an in-memory unconfirmed command's persistence first, or reloads the authoritative stored state when there is no such command.

## Inbound update gate

`UpdateService.CanProcessUpdates` delegates to the coordinator. A bot may process updates only when its runtime status is `Applied` and `AppliedEnabled` is true. The `TestBot` webhook controller checks that gate before calling `ProcessUpdate`; while state is unknown, pending, errored, or disabled, it returns HTTP 503 instead. The gate is closed as soon as a state-changing command begins, before the external webhook transition has completed.

After the gate allows an update, `TestBotUpdateService` routes commands, callback queries, animation messages, and events where the bot is added to a chat. The shared `UpdateService` builds command and callback maps from registered handlers: a matching key invokes its handler, while an unregistered key completes without a handler action. The `TestBot` POST controller resolves the keyed update service. Its GIF-save failure path logs the exception and sends an error notification. The clear-chat-data command limits its confirmation to private chats or administrators, and its callback checks chat membership again before clearing data.

## Focused tests

`BotControlCoordinatorTests` and `BotControlCoordinatorInitializationTests` cover persistence-before-apply behavior, startup restoration and seeding, per-bot concurrency, update gating, ambiguous writes, retries, and sanitized failure status. `MongoBotControlStateStoreTests` exercise the persistence adapter. `BotWebhookInitializerServiceTests` cover webhook transitions and resolver behavior. `TestBotUpdateServiceTests` and `UpdateControllerTests` cover update routing and the inbound gate. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md) for URL resolution and [Testing Strategy](../testing/strategy.md) for the test-project map.
