---
type: testing
title: Testing Strategy
description: Maps the NUnit test projects, shared test helpers, and representative behavioral coverage for host, bot runtime, persistence, and dashboard workflows.
tags: [testing, nunit, dotnet]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-03T08:54:46.183Z
sources:
  - id: openwiki-source-9ea84f99b1a810626d82a3a4
    resource: repo://BotFarm.sln
  - id: openwiki-source-1601dc4304e3854313f15d32
    resource: repo://Directory.Build.props
  - id: openwiki-source-ded4995a1847efcad19b2be1
    resource: repo://Directory.Packages.props
  - id: openwiki-source-cd1e88747b6a70de13f4ba8f
    resource: repo://tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
  - id: openwiki-source-87c5f84a91fd30f5c467624e
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotWebhookInitializerServiceTests.cs
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
  - id: openwiki-source-b1f2fc86ee9cc2aabfa4426b
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs
  - id: openwiki-source-302282d9cf72680d9594a2bb
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardComponentBaseTests.cs
  - id: openwiki-source-bc1578065985578f38e1d93e
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardStatsTests.cs
  - id: openwiki-source-f6698f132c6f37f801d47599
    resource: repo://tests/BotFarm.Shared.UnitTests/Utilities/FormatUtilsTests.cs
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
  - id: openwiki-source-945f3352a7a9ebfff648371a
    resource: repo://tests/BotFarm.UnitTests/ScheduledJobsRegistryTests.cs
  - id: openwiki-source-e2012c4a08961c92b5c9c92c
    resource: repo://tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs
  - id: openwiki-source-9652e011e303059aa5552ef0
    resource: repo://tests/TestBot.UnitTests/Handlers/Commands/GetLastGifCommandHandlerTests.cs
  - id: openwiki-source-cdcda05bbe88e211d4871ce7
    resource: repo://tests/TestBot.UnitTests/Health/TestBotStatsHealthCheckTests.cs
  - id: openwiki-source-7d844b4c635f1c89284dda9f
    resource: repo://tests/TestBot.UnitTests/Services/TestBotMarkupServiceTests.cs
  - id: openwiki-source-39c908a420b2fc3ea4e2c02c
    resource: repo://tests/TestBot.UnitTests/Services/TestBotUpdateServiceTests.cs
  - id: openwiki-source-9e4b45c38f8f8e4a834849f1
    resource: repo://tests/TestBot.UnitTests/TestBot.UnitTests.csproj
generated: { by: "copilot", at: "2026-10-03T08:54:46.183Z" }
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
| `BotFarm.UnitTests` | Host authentication and account setup, dashboard shutdown, scheduled jobs, and database shutdown. |
| `BotFarm.Shared.UnitTests` | Shared dashboard component behavior and formatting utilities. |
| `TestBot.UnitTests` | TestBot update controller, update/command flows, markup, GIF retrieval, and bot health behavior. |

`BotFarm.TestKit` is a support library referenced by the test projects. It provides Telegram client substitutes, message/update builders, outgoing-request assertions, fake HTTP handlers, a hybrid-cache scope, and temporary-directory helpers. Package versions are centrally declared in `Directory.Packages.props`; `Directory.Build.props` treats `CS4014` as an error.

## Representative behavioral checks

- `BotWebhookInitializerServiceTests` verifies that disabled bots are paused, enabled bots get the generated endpoint, base URL resolution is shared, and resolver order and missing-resolver failures are respected. The related design is documented in [Telegram Webhook Integration](../integrations/telegram-webhooks.md).
- MongoDB and backup tests cover cached chat settings, language defaults, failed reconnect shutdown, and restore outcomes such as pause failure, skipped empty collections, and resume after an exception. See [Persistence and Backups](../architecture/persistence-and-backups.md).
- `ScheduledJobsRegistryTests` checks the daily 05:00 backup schedule, per-bot backup invocation, host shutdown, and the optional periodic shutdown job.
- `DashboardControllerTests` checks shutdown responses and whether bot webhooks are paused when requested.
- `TestBotUpdateServiceTests` checks command and callback dispatch, GIF-save error notification, and the welcome message when the bot is added to a chat. See [Bot Runtime and Update Processing](../architecture/bot-runtime.md).

These tests exercise service and UI behavior with substitutes and test helpers. They do not replace verification against the deployed MongoDB, Telegram credentials, container health probes, or public webhook connectivity; see [Configuration and Operations](../operations/deployment.md) for those runtime boundaries.
