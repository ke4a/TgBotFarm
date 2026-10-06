---
type: operations
title: Configuration and Operations
description: Covers runtime configuration and bot-control state, Docker deployment, health and logging behavior, scheduled restarts, and persistence requirements for bot and control data.
tags: [operations, deployment, configuration, docker]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-06T14:43:11.351Z
sources:
  - id: openwiki-source-4726dde41bf8fa012078aa9f
    resource: repo://BotFarm.Core/Abstractions/BotService.cs
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-21cedf68ac5206cbb456e09a
    resource: repo://BotFarm.Core/Models/BotConfig.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-480fc983c32a8ad500166592
    resource: repo://BotFarm.Core/Services/LocalBackupHelperService.cs
  - id: openwiki-source-a06f6c23ee056cc162bc8d5a
    resource: repo://BotFarm.Core/Services/MongoBotControlStateStore.cs
  - id: openwiki-source-fda3ca8d1dc7d40f51b3c1ea
    resource: repo://BotFarm.Core/Services/MongoDbBackupService.cs
  - id: openwiki-source-8e7e36dadb3658bf71310d7d
    resource: repo://BotFarm/Dockerfile
  - id: openwiki-source-81baeca6a1a40fc83cf91310
    resource: repo://BotFarm/Health/MemoryHealthCheck.cs
  - id: openwiki-source-28081e19fa0768d84d8b5007
    resource: repo://BotFarm/nlog.config
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
  - id: openwiki-source-be91a71cde59d84cc1a91148
    resource: repo://BotFarm/ScheduledJobsRegistry.cs
  - id: openwiki-source-5a967c2a70e92f59eae7aebf
    resource: repo://BotFarm/Startup.cs
  - id: openwiki-source-7b43f8d2fe7b509c09f79842
    resource: repo://docker-compose.override.yml
  - id: openwiki-source-b79fbbd921df689b4bbdc82f
    resource: repo://docker-compose.yml
  - id: openwiki-source-b273e7de5ce8bb6689e05057
    resource: repo://TestBot/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-945f3352a7a9ebfff648371a
    resource: repo://tests/BotFarm.UnitTests/ScheduledJobsRegistryTests.cs
generated: { by: "copilot", at: "2026-10-06T14:43:11.351Z" }
---

# Configuration and Operations

## Configuration inputs

`Startup` builds configuration from `appsettings.json`, an optional `appsettings.<Environment>.json`, and environment variables. Environment variables can override JSON configuration. Use a secure configuration provider or deployment secrets for credentials; do not put live values in this wiki or commit them to source.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:MongoDb` | MongoDB connection used by bot persistence and the web host's Identity store. Identity uses the `BotFarmIdentity` database name. |
| `Bots:TestBot:BotConfig` | Named TestBot settings. `BotConfig` includes `Token`, `Emoji`, `Handle`, and `AdminChatId`; the bot service requires a token. |
| `Bots:TestBot:BotConfig:Enabled` | Bootstrap fallback used only when the durable bot-control record does not yet exist. The strongly typed `BotConfig` no longer owns this flag; after initial seeding, the `BotFarmControl` MongoDB record is authoritative. |
| `WebHookUrl` | A literal HTTPS base URL or a provider keyword recognized by the webhook resolvers. Enabled bots receive a route below this URL. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md). |
| `ScheduledJobs:ShutdownEveryHours` | Optional periodic shutdown interval. A value greater than zero adds a recurring shutdown job. |
| `ASPNETCORE_ENVIRONMENT` | Selects Development or Production host behavior, including whether tunnel webhook resolvers are registered. |

The production Compose file passes `ConnectionStrings__MongoDb` and `WebHookUrl` from external environment values. It does not supply a bot token; provide the named bot configuration through an appropriate secure configuration source.

## Container deployment

`BotFarm/Dockerfile` builds and publishes the .NET 10 web app in a multi-stage image, runs it as a non-root user, and listens on container port `5000`. The production `docker-compose.yml` maps host port `9090` to that port, sets the environment to Production, and uses the `unless-stopped` restart policy.

The development `docker-compose.override.yml` adds MongoDB 8.3.11 with data/config volumes and a tunnel container, and mounts host `logs` and `backups` directories into the app container. It selects Development and defaults the tunnel provider to localtunnel. Treat this override as local-development-only; provide environment-specific credentials through a secure source for shared or production deployments.

The tunnel image supports localtunnel and ngrok. `TUNNEL_PROVIDER` selects the client and must agree with the app's `WebHookUrl`; ngrok additionally requires its authentication token. For provider discovery and retry behavior, see [Telegram Webhook Integration](../integrations/telegram-webhooks.md).

## Scheduled jobs and shutdown

`Program.Main` starts scheduled jobs before starting the web host. After the host starts, it initializes bot control state for each registered bot. When a control record does not exist yet, the coordinator seeds desired enabled state from `Bots:<bot-name>:BotConfig:Enabled`; future startups use the durable `BotFarmControl` record. Each bot's inbound updates remain gated until its desired state is confirmed as applied and enabled. A webhook transition error is surfaced in bot status and can be retried from the dashboard rather than being reported as success.

`ScheduledJobsRegistry` creates a daily backup job at 05:00. It calls `BackupDatabase` for every registered bot and then requests application shutdown. The job does not branch on each backup `Result` before stopping the process. A positive `ScheduledJobs:ShutdownEveryHours` setting adds a separate periodic shutdown job.

The production Compose service's restart policy is configured to restart an exited container unless it has been explicitly stopped. On graceful host shutdown, `DatabaseShutdownHostedService` disconnects each registered database service; the host allows up to 25 seconds for shutdown. The archive format, restore behavior, and retention policy are detailed in [Persistence and Backups](../architecture/persistence-and-backups.md).

Backup archives are written under the application's `backups/<bot-name>/` directory. The production Compose file does not mount that directory as a volume, so deployments that need archives to survive container replacement must provide persistent storage. The development override does mount it.

Bot archives cover each registered bot database, not the separate `BotFarmControl` database. Include that control database in any MongoDB-level backup or recovery procedure that must preserve operator-selected bot state. See [Persistence and Backups](../architecture/persistence-and-backups.md) for database ownership and restore boundaries.

## Health checks and logs

The app maps `/health` and `/health-ui`, both protected by `HEALTH_CHECKS_UI_POLICY`. The HealthChecks UI supplies an internal process-local API key when it calls `/health`. The Dockerfile and production Compose health probes instead run `wget` against `/health` without that header or a login cookie. Validate the probe's response behavior in the target image/runtime; an unauthenticated probe may not represent the health-check result.

Health checks include process memory usage and application uptime. Memory usage at or above the configured options threshold reports unhealthy status and sends a warning notification; the default threshold in `MemoryCheckOptions` is approximately 400 MB.

NLog writes Information-and-higher logs to console and to `logs/nlog-all.log` and `logs/nlog-own.log`; file targets archive daily and retain up to seven archive files. The development Compose override mounts the log directory for host access.

See [System Overview](../architecture/system-overview.md) for host composition and [Testing Strategy](../testing/strategy.md) for coverage of the scheduled-job behavior.
