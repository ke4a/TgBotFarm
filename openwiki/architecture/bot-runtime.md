---
type: architecture
title: Bot Runtime and Update Processing
description: Explains how BotFarm configures named bot instances, routes Telegram updates through keyed services, and separates shared update dispatch from TestBot-specific behavior.
tags: [architecture, bots, updates]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-03T08:54:46.183Z
sources:
  - id: openwiki-source-4726dde41bf8fa012078aa9f
    resource: repo://BotFarm.Core/Abstractions/BotService.cs
  - id: openwiki-source-48906944d9d67f8e9f27c2b4
    resource: repo://BotFarm.Core/Abstractions/UpdateService.cs
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
  - id: openwiki-source-39c908a420b2fc3ea4e2c02c
    resource: repo://tests/TestBot.UnitTests/Services/TestBotUpdateServiceTests.cs
generated: { by: "copilot", at: "2026-10-03T08:54:46.183Z" }
---

# Bot Runtime and Update Processing

## Runtime ownership

BotFarm's web host owns process startup and asks the registered `IBotWebhookInitializer` to initialize all bot services before starting the scheduled jobs and serving requests. Shared bot lifecycle behavior lives in `BotFarm.Core`; the `TestBot` project supplies one concrete implementation and is registered into the same host. `TestBot` is an example bot implementation, not a separate executable.

The core `BotService` base class reads the named `BotConfig` for its `BotIdentity.Name`, requires a token, creates a Telegram client through `ITelegramBotClientFactory`, and captures whether the bot is enabled. Its initialization creates a bot-specific temporary directory and fetches the Telegram bot identity. The same base class owns setting, deleting, and reapplying the webhook. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md) for URL resolution and startup behavior.

## Named configuration and keyed services

`TestBot` registers a stable `BotIdentity`, binds its named `BotConfig` from the `Bots:TestBot:BotConfig` configuration section, and registers the bot, update processor, command handlers, and callback handlers with the `TestBot` DI key. It also exposes selected database and bot services without a key for host-wide operations that iterate all registered bots. `BotRegistry` provides name-based access to keyed services such as `IBotService` and `IMongoDbDatabaseService`.

This arrangement keeps dependencies for a specific bot scoped to that bot's key while letting shared host services enumerate all `IBotService` instances. The current repository wires one bot; adding another requires its own identity, options binding, keyed services, and update endpoint.

## Inbound update flow

1. Telegram sends an update to the bot's HTTP endpoint. The `TestBot` `UpdateController` accepts `POST /api/TestBot/Update`, receives its `IUpdateService` using the `TestBot` key, and forwards the deserialized update to `ProcessUpdate`.
2. `TestBotUpdateService` classifies the update. It handles bot commands in private chats or commands directed to the configured bot handle, callback queries, animation messages, and group-member events where the bot itself was added.
3. For commands and callbacks, the service loads the chat language and handles shared `/start` and language-change behavior directly. Other commands and callback keys go through the shared `UpdateService` dispatch helpers.
4. `UpdateService` builds command and callback dictionaries from the keyed handler registrations. A matching key invokes its handler; an unregistered key completes without a handler action. The shared base also provides helpers for language changes and welcome messages.

TestBot's separate command handlers illustrate bot-specific behavior: `/getlastgif` retrieves the requesting user's last stored animation in the current chat, while `/clearchatdata` requests confirmation and only offers the clearing action to a chat administrator (or in a private chat). Its callback handler checks the user's chat membership before clearing the chat's stored data.

Animation persistence errors are logged and sent through `INotificationService`; the update handler otherwise keeps Telegram-specific command and event routing separate from the reusable dispatch base. This separation lets a new bot provide its own `IUpdateService`, keyed handlers, and database while reusing the shared service abstractions.

## Focused tests

`TestBotUpdateServiceTests` verifies command and callback dispatch, notification on GIF-save failure, and the welcome message when the bot is added to a chat. `BotWebhookInitializerServiceTests` covers enabled/disabled bot startup and resolver behavior; the webhook details are documented separately. See [Testing Strategy](../testing/strategy.md) for the solution's test-project map.
