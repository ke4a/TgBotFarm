---
type: testing
title: Testing Strategy
description: Maps the NUnit test projects, shared test helpers, and representative behavioral coverage for host, bot runtime, persistence, and dashboard workflows.
tags: [testing, nunit, dotnet]
sources:
  - id: openwiki-source-9ea84f99b1a810626d82a3a4
    resource: repo://BotFarm.sln
  - id: openwiki-source-1601dc4304e3854313f15d32
    resource: repo://Directory.Build.props
  - id: openwiki-source-ded4995a1847efcad19b2be1
    resource: repo://Directory.Packages.props
  - id: openwiki-source-cd1e88747b6a70de13f4ba8f
    resource: repo://tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
  - id: openwiki-source-84fd40ef616413cb117d41e1
    resource: repo://tests/BotFarm.Core.UnitTests/Helpers/TgUserAuthorizationHelperTests.cs
  - id: openwiki-source-28b96d7707a581693978046f
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorInitializationTests.cs
  - id: openwiki-source-d0f413eb6f380ffa2d995247
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs
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
  - id: openwiki-source-fbe7280ec5a660af9d6284f1
    resource: repo://tests/BotFarm.Shared.UnitTests/BotFarm.Shared.UnitTests.csproj
  - id: openwiki-source-3af48019bf47409f851d1595
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardBackupsTests.cs
  - id: openwiki-source-bc1578065985578f38e1d93e
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardStatsTests.cs
  - id: openwiki-source-a8ab47feb2838969804ee63a
    resource: repo://tests/BotFarm.TestKit/FakeHttpMessageHandler.cs
  - id: openwiki-source-ab6c3b5a5ac1bf6c095fd2bc
    resource: repo://tests/BotFarm.TestKit/HybridCacheScope.cs
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
  - id: openwiki-source-889e09924fd5ec44035c4b38
    resource: repo://tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj
  - id: openwiki-source-650fcd1aa895f9944337719d
    resource: repo://tests/BotFarm.UnitTests/Controllers/DashboardControllerTests.cs
  - id: openwiki-source-70bbaf19137fa04a3b46a9a0
    resource: repo://tests/BotFarm.UnitTests/HostedServices/DatabaseShutdownHostedServiceTests.cs
  - id: openwiki-source-a9825a65f9f1dcaadd450586
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/LoginModelTests.cs
  - id: openwiki-source-f58a8af365797e8a1d53e668
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/SetupModelTests.cs
  - id: openwiki-source-a553b821cb052209d4dbdfa1
    resource: repo://tests/BotFarm.UnitTests/Pages/DashboardBotControlTests.cs
  - id: openwiki-source-945f3352a7a9ebfff648371a
    resource: repo://tests/BotFarm.UnitTests/ScheduledJobsRegistryTests.cs
  - id: openwiki-source-e2012c4a08961c92b5c9c92c
    resource: repo://tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs
  - id: openwiki-source-6a6e69b2f93c70aea3f45fde
    resource: repo://tests/TestBot.UnitTests/Handlers/Callbacks/ClearChatDataCallbackHandlerTests.cs
  - id: openwiki-source-f57a17c79db9dc161a3e8d8e
    resource: repo://tests/TestBot.UnitTests/Handlers/Commands/ClearChatDataCommandHandlerTests.cs
  - id: openwiki-source-cdcda05bbe88e211d4871ce7
    resource: repo://tests/TestBot.UnitTests/Health/TestBotStatsHealthCheckTests.cs
  - id: openwiki-source-9e4b45c38f8f8e4a834849f1
    resource: repo://tests/TestBot.UnitTests/TestBot.UnitTests.csproj
generated: { by: "copilot", at: "2026-10-14T09:31:37.432Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-10-14T09:31:37.432Z
---

# Testing Strategy

## Test projects

The solution targets .NET 10 and contains four NUnit test projects. Run the complete suite from the repository root with:

```sh
dotnet test BotFarm.sln
```

For a focused run, pass one test project instead of the solution:

```sh
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
```

| Project | Main coverage |
| --- | --- |
| `BotFarm.Core.UnitTests` | Bot lifecycle and registry, webhook initialization and URL resolvers, MongoDB connection and chat-settings behavior, backups, localization, notifications, and markup. |
| `BotFarm.UnitTests` | Host authentication and account setup, bot-control dashboard actions, dashboard shutdown, scheduled jobs, and database shutdown. |
| `BotFarm.Shared.UnitTests` | Shared dashboard component behavior and formatting utilities. |
| `TestBot.UnitTests` | TestBot update controller, update/command flows, markup, GIF retrieval, and bot health behavior. |

`BotFarm.TestKit` is a support library referenced by the test projects. It provides Telegram client substitutes, message/update builders, outgoing-request assertions, fake HTTP handlers, an async-enumerable factory, a hybrid-cache scope, and temporary-directory helpers. Package versions are centrally declared in `Directory.Packages.props`; `Directory.Build.props` treats `CS4014` as an error.

## Bot lifecycle control checks

- `BotControlCoordinatorInitializationTests` checks first-state seeding, saved-state precedence over changed configuration, handling of concurrent inserts, unknown/gated status when the store is unavailable, retry after recovery, and cancellation. `BotControlCoordinatorTests` covers persistence before apply, ambiguous writes, per-bot serialization, update gating, retry exhaustion, and sanitized failures. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md).
- `MongoBotControlStateStoreTests` verifies state insert/update behavior, lazy database factory retry, and deserialization of older documents containing the removed `Revision` field. See [Persistence and Backups](../architecture/persistence-and-backups.md).
- `BotWebhookInitializerServiceTests` verifies that enabling initializes before resolving and setting the webhook, disabling delegates to pause, resolver registration order is honored, and a missing resolver fails. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md).
- `DashboardBotControlTests` checks pending/applied/unknown/error rendering, explicit toggle and retry calls, retryable failure feedback, and disabled switches until state is known. `UpdateControllerTests` verifies that a closed bot-control gate returns HTTP 503 without invoking update processing.

## Other representative behavioral checks

- MongoDB tests cover estimated versus exact counts, cached chat settings, language defaults, and reconnect failure shutdown. Backup tests verify pause refusal, batches of 500 documents, empty-collection replacement, cleanup and target preservation when staging or rename fails, and bot resume after restore exceptions. See [Persistence and Backups](../architecture/persistence-and-backups.md).
- `TgUserAuthorizationHelperTests` cover private-chat access, group administrator lookup, and use of the callback sender. Clear-chat command and callback tests verify confirmation, denial, and refusal to clear data for a non-admin. `TestBotStatsHealthCheckTests` verify healthy chat-count data and unhealthy results when the query fails; `CoreServicesDependencyInjectionTests` verify Mongo client singleton registration and failure when its connection string is missing. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) and [Configuration and Operations](../operations/deployment.md).
- `ScheduledJobsRegistryTests` checks the daily 05:00 backup schedule, per-bot backup invocation, host shutdown, and the optional periodic shutdown job.
- `DashboardControllerTests` checks shutdown responses and whether bot webhooks are paused when requested.
- `DashboardBackupsTests` cover backup progress, duplicate-operation prevention, confirmations, result feedback, and newest-first listing. `DashboardStatsTests` cover selected database lookup, refresh, loading reset, and error feedback. See [Operator Dashboard](../workflows/operator-dashboard.md).
- `TestBotUpdateServiceTests` checks command and callback dispatch, GIF-save error notification, and the welcome message when the bot is added to a chat. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md).

These tests exercise service and UI behavior with substitutes and test helpers. They do not replace verification against the deployed MongoDB, Telegram credentials, container health probes, or public webhook connectivity; see [Configuration and Operations](../operations/deployment.md) for those runtime boundaries.
