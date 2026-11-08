---
type: testing
title: Testing Strategy
description: Maps BotFarm's NUnit test projects, the shared TestKit helpers, how to run tests locally and in CI, and the focused tests that pin down bot modules, configuration validation, webhook delivery, lifecycle control, hosted services, and backups.
tags: [testing, nunit, ci]
sources:
  - id: openwiki-source-9ea84f99b1a810626d82a3a4
    resource: repo://BotFarm.sln
  - id: openwiki-source-1601dc4304e3854313f15d32
    resource: repo://Directory.Build.props
  - id: openwiki-source-ded4995a1847efcad19b2be1
    resource: repo://Directory.Packages.props
  - id: openwiki-source-cd1e88747b6a70de13f4ba8f
    resource: repo://tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
  - id: openwiki-source-e78abac2f575fb255596329c
    resource: repo://tests/BotFarm.Core.UnitTests/Extensions/BotConfigRegistrationTests.cs
  - id: openwiki-source-84fd40ef616413cb117d41e1
    resource: repo://tests/BotFarm.Core.UnitTests/Helpers/TgUserAuthorizationHelperTests.cs
  - id: openwiki-source-28b96d7707a581693978046f
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorInitializationTests.cs
  - id: openwiki-source-d0f413eb6f380ffa2d995247
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs
  - id: openwiki-source-c8127523d71a771d2c7d35c1
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotModuleDiscoveryTests.cs
  - id: openwiki-source-87c5f84a91fd30f5c467624e
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotWebhookInitializerServiceTests.cs
  - id: openwiki-source-3a8c43e08d5c8e303fca443f
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoBotControlStateStoreTests.cs
  - id: openwiki-source-8e270948a1f32961a4a6e0c7
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoChatSettingsRepositoryTests.cs
  - id: openwiki-source-af0728da3be4c85f0b8da66c
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoConnectionManagerTests.cs
  - id: openwiki-source-2dae7d6c8c6c0c7b1c6b4e43
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoDbBackupServiceTests.cs
  - id: openwiki-source-994de5f20c7946d212aa2b30
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoDbDatabaseServiceTests.cs
  - id: openwiki-source-fdf959d5f24404f773962876
    resource: repo://tests/BotFarm.Core.UnitTests/Services/UpdateDeliveryGuardTests.cs
  - id: openwiki-source-2e24eb17d0d649cf91303988
    resource: repo://tests/BotFarm.Core.UnitTests/Services/UpdateServiceTests.cs
  - id: openwiki-source-fbe7280ec5a660af9d6284f1
    resource: repo://tests/BotFarm.Shared.UnitTests/BotFarm.Shared.UnitTests.csproj
  - id: openwiki-source-3af48019bf47409f851d1595
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardBackupsTests.cs
  - id: openwiki-source-b1f2fc86ee9cc2aabfa4426b
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs
  - id: openwiki-source-bc1578065985578f38e1d93e
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardStatsTests.cs
  - id: openwiki-source-fdbfab24f89c92932bc42300
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/SendMessageDialogRenderTests.cs
  - id: openwiki-source-45a31912f62dd4487f510764
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/SendMessageDialogTests.cs
  - id: openwiki-source-a8ab47feb2838969804ee63a
    resource: repo://tests/BotFarm.TestKit/FakeHttpMessageHandler.cs
  - id: openwiki-source-ab6c3b5a5ac1bf6c095fd2bc
    resource: repo://tests/BotFarm.TestKit/HybridCacheScope.cs
  - id: openwiki-source-c290bd58b9a341e85a0bbbd7
    resource: repo://tests/BotFarm.TestKit/ManualTimeProvider.cs
  - id: openwiki-source-dfbc51617658d4d819503545
    resource: repo://tests/BotFarm.TestKit/TelegramBotClientFactory.cs
  - id: openwiki-source-a4cbd582baf6076937714cb6
    resource: repo://tests/BotFarm.TestKit/TelegramMessageFactory.cs
  - id: openwiki-source-522c50916b08a50ad5220ab6
    resource: repo://tests/BotFarm.TestKit/TelegramRequestAssertHelpers.cs
  - id: openwiki-source-b9ab1357786a324a34ea7168
    resource: repo://tests/BotFarm.TestKit/TempDirectoryScope.cs
  - id: openwiki-source-c619bea31717881241eb6750
    resource: repo://tests/BotFarm.UnitTests/Authentication/ApiKeyAuthenticationHandlerTests.cs
  - id: openwiki-source-589566a8335d3d25db40fe52
    resource: repo://tests/BotFarm.UnitTests/Authentication/SetupCodeServiceTests.cs
  - id: openwiki-source-889e09924fd5ec44035c4b38
    resource: repo://tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj
  - id: openwiki-source-650fcd1aa895f9944337719d
    resource: repo://tests/BotFarm.UnitTests/Controllers/DashboardControllerTests.cs
  - id: openwiki-source-2e5d2bc4f5af7a376352e2ea
    resource: repo://tests/BotFarm.UnitTests/HostedServices/NightlyBackupServiceTests.cs
  - id: openwiki-source-0a864b3b845dda89285151c9
    resource: repo://tests/BotFarm.UnitTests/HostedServices/PeriodicShutdownServiceTests.cs
  - id: openwiki-source-a1afafc6c31904d5b8db18f6
    resource: repo://tests/BotFarm.UnitTests/HostedServices/SetupAnnouncementHostedServiceTests.cs
  - id: openwiki-source-173284f4cbd21f83bdb77406
    resource: repo://tests/BotFarm.UnitTests/Localization/LanguageFileKeyParityTests.cs
  - id: openwiki-source-a9825a65f9f1dcaadd450586
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/LoginModelTests.cs
  - id: openwiki-source-300ed746cad3a604192471a5
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/LogoutModelTests.cs
  - id: openwiki-source-f58a8af365797e8a1d53e668
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/SetupModelTests.cs
  - id: openwiki-source-a553b821cb052209d4dbdfa1
    resource: repo://tests/BotFarm.UnitTests/Pages/DashboardBotControlTests.cs
  - id: openwiki-source-e2012c4a08961c92b5c9c92c
    resource: repo://tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs
  - id: openwiki-source-6a6e69b2f93c70aea3f45fde
    resource: repo://tests/TestBot.UnitTests/Handlers/Callbacks/ClearChatDataCallbackHandlerTests.cs
  - id: openwiki-source-f57a17c79db9dc161a3e8d8e
    resource: repo://tests/TestBot.UnitTests/Handlers/Commands/ClearChatDataCommandHandlerTests.cs
  - id: openwiki-source-cdcda05bbe88e211d4871ce7
    resource: repo://tests/TestBot.UnitTests/Health/TestBotStatsHealthCheckTests.cs
  - id: openwiki-source-1aa36b0f627137c2408dc39b
    resource: repo://tests/TestBot.UnitTests/Services/TestBotDatabaseServiceTests.cs
  - id: openwiki-source-9e4b45c38f8f8e4a834849f1
    resource: repo://tests/TestBot.UnitTests/TestBot.UnitTests.csproj
generated: { by: "claude-code", at: "2026-11-08T22:37:15.271Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-08T22:37:15.271Z
---

# Testing Strategy

## Test projects

The solution targets .NET 10 and has four NUnit test projects, which use NSubstitute for substitutes. The host and Shared test projects also reference bUnit to render Blazor components and click through them. Run the whole suite from the repository root:

```sh
dotnet test BotFarm.sln
```

For a focused run, pass one project, optionally with a filter:

```sh
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj --filter "FullyQualifiedName~UpdateDeliveryGuard"
```

| Project | Main coverage |
| --- | --- |
| `BotFarm.Core.UnitTests` | Module discovery and registry, `BotConfig` validation, bot control and state store, webhook initializer, URL resolvers and secrets, the delivery guard and transient-error classification, update routing, MongoDB services, backups, data paths, localization, markup, and notifications. |
| `BotFarm.UnitTests` | Host composition (`WebApplicationExtensionsTests`), authentication handlers, the setup code and account pages, the dashboard controller and bot-control panel (including bUnit render tests), sign-out, language-file key parity, hosted services, health checks, and NLog configuration. |
| `BotFarm.Shared.UnitTests` | Dashboard components (including bUnit render tests of the send-message dialog), the component base class, the main layout, and formatting utilities. |
| `TestBot.UnitTests` | The TestBot module, update controller, update service, handlers, markup, database service, and health check. |

`BotFarm.TestKit` is a support library shared by the test projects. It provides Telegram client and message factories, outgoing-request assertions, a fake HTTP handler, Mongo cursor and async-enumerable factories, a `HybridCache` scope, temporary-directory scopes, a reflection helper, a bot-registry factory, and `ManualTimeProvider`. That clock lets tests fire timers and delays deterministically with `Advance`, which the hosted-service, delivery-guard, and alert-throttle tests rely on. Package versions are centrally declared in `Directory.Packages.props`. `Directory.Build.props` treats `CS4014` (an unawaited call) as an error.

CI (`.github/workflows/ci.yml`) restores, builds in Release, and runs `dotnet test BotFarm.sln` on every push to `master` and on every pull request; see [Configuration and Operations](../operations/deployment.md).

## Bot modules and configuration

- `BotModuleDiscoveryTests` cover creating each declared module once, skipping assemblies without the attribute, invalid module types, name ordering, rejecting duplicate names that differ only in case, and distinct component assemblies. `BotRegistryTests` cover keyed lookups and module-order enumeration. `TestBotModuleTests` and `WebApplicationExtensionsTests` check that the host finds TestBot and registers its services keyed by name. See [Adding a Bot Module](../workflows/adding-a-bot.md).
- `BotConfigValidatorTests` cover required and malformed tokens, webhook-secret rules, the required admin chat ID, and reporting several problems at once; `BotConfigRegistrationTests` cover start-time validation.

## Webhook delivery

- `UpdateControllerTests` cover rejecting a wrong secret before the readiness check, the 503 gate, duplicate suppression, redelivery after 401/503, transient retries up to the drop limit, failure-count reset, permanent Telegram rejections, and alerts throttled to one per minute. `UpdateDeliveryGuardTests`, `WebhookSecretProviderTests`, and `TransientErrorsTests` cover the guard's windows and capacity, configured and derived secrets, and error classification. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md).
- `BotWebhookInitializerServiceTests` and the resolver tests cover enable/disable order, resolver precedence, missing resolvers, HTTPS validation, environment variables, and ngrok retries.

## Bot lifecycle control

- `BotControlCoordinatorInitializationTests` cover seeding missing state from configuration, saved state taking precedence, concurrent seeding, unknown and gated status when the store is unavailable, retry after recovery, and cancellation. `BotControlCoordinatorTests` cover persistence before apply, ambiguous writes, per-bot serialization, gating, retry exhaustion, maintenance pauses, and sanitized failures. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md).
- `MongoBotControlStateStoreTests` cover insert, read, and update, lazy database creation that retries after a factory failure, and deserializing records that still contain a removed `Revision` field.
- `BotControlStartupServiceTests` cover waiting for application start. `DashboardBotControlTests` cover status rendering, explicit toggle and retry calls, retryable feedback, and switches disabled until state is known; `DashboardBotControlRenderTests` render the panel with bUnit and check the applying state while a toggle runs, retry buttons for failed or unconfirmed bots, and the empty message. `DashboardControllerTests` check that bots are paused before the host stops when requested.

## Hosted services, setup, and operations

- `NightlyBackupServiceTests` cover next-run calculation across time zones and daylight-saving transitions, invalid settings, continuing after one bot fails, alert failures, and cancellation. `PeriodicShutdownServiceTests` cover the interval and stopping early.
- `SetupCodeServiceTests` cover generated and configured codes, case-sensitive verification, invalidating the code after success, and one admin from concurrent attempts. `SetupAnnouncementHostedServiceTests` cover logging the code without revealing a configured value and surviving an unreachable identity store. `SetupModelTests` and `LoginModelTests` cover the first-run redirects and sign-in. `LogoutModelTests` check that a GET only redirects home, a POST signs out, and the page does not opt out of antiforgery validation. See [Operator Dashboard and Administration](../workflows/operator-dashboard.md).
- `NLogConfigTests` and `AppDataPathsTests` cover the data-folder layout. `LanguageFileKeyParityTests` read every bot directory under the copied `Languages` folder and fail when a locale misses a key another locale defines or has an empty value. `MemoryHealthCheckTests` and `AppStatsHealthCheckTests` cover health data.

## Persistence and UI

- `MongoConnectionManagerTests`, `MongoChatSettingsRepositoryTests`, and `MongoDbBackupServiceTests` cover count selection, cached chat settings and language defaults, moving chat settings to a new chat ID (missing source, kept existing target, source kept when the insert fails), alerts on failed collection operations, refusal when pausing fails, partial-archive cleanup, 500-document restore batches, empty-collection replacement, and target preservation when staging or rename fails. See [Persistence and Backups](../architecture/persistence-and-backups.md).
- `MongoDbDatabaseServiceTests` cover the `MigrateChat` order (settings before the bot hook), equal chat IDs, and failure propagation; `UpdateServiceTests` check that the new-supergroup notice moves data before calling the hook once, the old-group notice does not migrate, and a failed move is logged, rethrown, and skips the hook. `TestBotDatabaseServiceTests` and `GifDataSerializationTests` cover the single `GifData` collection: the `ChatId`-first compound key, the duplicate-key save retry, chat-range deletes that never drop a collection, and GIF migration that keeps existing target records.
- `TelegramNotificationServiceTests` cover HTML escaping and message-length truncation for admin alerts. `TgUserAuthorizationHelperTests` and the clear-chat handler tests cover private chats, administrator lookup, callback-sender checks, and denials.
- Shared component tests cover backup progress, duplicate-create prevention, confirmations, newest-first listing, statistics refresh and load errors, and the dark-mode preference in the layout. `DashboardChatsTests` cover chat paging that fetches Telegram details only for the requested page, marks unavailable chats and retries them on the next load, reuses cached names until a refresh, and keeps order and numbering when parallel fetches finish out of order; they also cover clipboard copy and opening the send-message dialog. `SendMessageDialogTests` and `SendMessageDialogRenderTests` cover editor module initialization, sending HTML and closing, warnings for an empty message, errors that keep the dialog open, ignoring a second send while one is in flight, cancel, and module disposal.

These tests use substitutes and helpers. They do not replace checks against real MongoDB, Telegram credentials, container health probes, or public webhook connectivity.
