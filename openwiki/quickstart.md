---
type: guide
title: Quickstart
description: Gets contributors oriented to the .NET solution, local run and test commands, required configuration, and the wiki pages for deeper workflows.
tags: [quickstart, development, dotnet]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-03T08:54:46.183Z
sources:
  - id: openwiki-source-4726dde41bf8fa012078aa9f
    resource: repo://BotFarm.Core/Abstractions/BotService.cs
  - id: openwiki-source-21cedf68ac5206cbb456e09a
    resource: repo://BotFarm.Core/Models/BotConfig.cs
  - id: openwiki-source-9ea84f99b1a810626d82a3a4
    resource: repo://BotFarm.sln
  - id: openwiki-source-fea3c9751b25c983e22882a4
    resource: repo://BotFarm/Authentication/DevelopmentAuthenticationHandler.cs
  - id: openwiki-source-1ec73b08b7d229c529b32a43
    resource: repo://BotFarm/BotFarm.csproj
  - id: openwiki-source-8e7e36dadb3658bf71310d7d
    resource: repo://BotFarm/Dockerfile
  - id: openwiki-source-d7a307bf5764d5ffcff4415d
    resource: repo://BotFarm/Pages/Account/Login.cshtml.cs
  - id: openwiki-source-54f57d8ffb92cfc1015f6a01
    resource: repo://BotFarm/Pages/Account/Setup.cshtml.cs
  - id: openwiki-source-5a967c2a70e92f59eae7aebf
    resource: repo://BotFarm/Startup.cs
  - id: openwiki-source-7b43f8d2fe7b509c09f79842
    resource: repo://docker-compose.override.yml
  - id: openwiki-source-b79fbbd921df689b4bbdc82f
    resource: repo://docker-compose.yml
  - id: openwiki-source-b273e7de5ce8bb6689e05057
    resource: repo://TestBot/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-cd1e88747b6a70de13f4ba8f
    resource: repo://tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
  - id: openwiki-source-fbe7280ec5a660af9d6284f1
    resource: repo://tests/BotFarm.Shared.UnitTests/BotFarm.Shared.UnitTests.csproj
  - id: openwiki-source-889e09924fd5ec44035c4b38
    resource: repo://tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj
  - id: openwiki-source-9e4b45c38f8f8e4a834849f1
    resource: repo://tests/TestBot.UnitTests/TestBot.UnitTests.csproj
generated: { by: "copilot", at: "2026-10-03T08:54:46.183Z" }
---

# Quickstart

BotFarm is a .NET 10 solution. Start with `BotFarm.sln` for the web host, reusable libraries, reference TestBot, and test projects. The host needs MongoDB and a configured bot service; use environment-specific settings or a secret provider for credentials rather than committing live values.

## Configure a local run

The host reads `appsettings.json`, optional `appsettings.<Environment>.json`, and environment variables. Before starting, provide:

- `ConnectionStrings:MongoDb` for MongoDB.
- `Bots:TestBot:BotConfig` for the named bot settings (`Enabled`, `Token`, `Emoji`, `Handle`, and `AdminChatId`). The bot service requires a token.
- `WebHookUrl` when a bot is enabled. Use an absolute HTTPS base URL or a supported development tunnel provider. See [Telegram Webhook Integration](./integrations/telegram-webhooks.md).

Then run the host:

```sh
dotnet run --project BotFarm/BotFarm.csproj
```

For the local Docker Compose setup, `docker-compose.override.yml` adds MongoDB and a webhook tunnel to the app. It defaults the tunnel provider to localtunnel and mounts host `logs` and `backups` directories. Production uses `docker-compose.yml` alone and requires externally supplied MongoDB and webhook settings; provide the bot's named configuration separately.

## Build and test

From the repository root:

```sh
dotnet build BotFarm.sln
dotnet test BotFarm.sln
```

To run only the Core unit tests:

```sh
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
```

All test projects target .NET 10 and use NUnit. See [Testing Strategy](./testing/strategy.md) for the test-project map and focused coverage.

## First dashboard visit

In Production, if no Identity user exists, `/Account/Login` redirects to `/Account/Setup` to create the first account. Later visits use the login page. Development uses a synthetic developer identity. The authenticated dashboard links to host health/logs and registered bot pages; see [Operator Dashboard and Administration](./workflows/operator-dashboard.md).

## Wiki map

- [System Overview](./architecture/system-overview.md) — project boundaries, service composition, and host lifecycle.
- [Bot Runtime and Update Processing](./architecture/bot-runtime.md) — keyed bot services and inbound update dispatch.
- [Persistence and Backups](./architecture/persistence-and-backups.md) — MongoDB ownership, chat settings, and archive behavior.
- [Configuration and Operations](./operations/deployment.md) — container deployment, scheduled jobs, health checks, and logs.
