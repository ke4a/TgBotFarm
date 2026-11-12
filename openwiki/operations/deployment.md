---
type: operations
title: Configuration and Operations
description: Lists BotFarm's configuration keys and startup validation, the container image and Compose setup with its persistent data volume, liveness and health endpoints with the uptime-monitor API key, scheduled backup and shutdown services, logging, and CI.
tags: [operations, deployment, configuration, docker]
sources:
  - id: openwiki-source-715dace563ef484b6e8bd1e2
    resource: repo://.dockerignore
  - id: openwiki-source-164e2da859b5277df81c7d94
    resource: repo://.github/workflows/ci.yml
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-a8ad2bd5db955bd1890a707c
    resource: repo://BotFarm.Core/Health/BotStatsHealthCheck.cs
  - id: openwiki-source-ef49977dd991e45175722186
    resource: repo://BotFarm.Core/Health/BotStatsHealthCheckBuilderExtensions.cs
  - id: openwiki-source-21cedf68ac5206cbb456e09a
    resource: repo://BotFarm.Core/Models/BotConfig.cs
  - id: openwiki-source-554d6aed4058b870daf58f39
    resource: repo://BotFarm.Core/Services/AppDataPaths.cs
  - id: openwiki-source-06c01f5e5d0d64d6d046c258
    resource: repo://BotFarm.Core/Services/BotConfigValidator.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-480fc983c32a8ad500166592
    resource: repo://BotFarm.Core/Services/LocalBackupHelperService.cs
  - id: openwiki-source-a06f6c23ee056cc162bc8d5a
    resource: repo://BotFarm.Core/Services/MongoBotControlStateStore.cs
  - id: openwiki-source-fda3ca8d1dc7d40f51b3c1ea
    resource: repo://BotFarm.Core/Services/MongoDbBackupService.cs
  - id: openwiki-source-7c9899e41ca0257a23907b10
    resource: repo://BotFarm/Authentication/HealthApiKeyAuthenticationHandler.cs
  - id: openwiki-source-1ec73b08b7d229c529b32a43
    resource: repo://BotFarm/BotFarm.csproj
  - id: openwiki-source-8e7e36dadb3658bf71310d7d
    resource: repo://BotFarm/Dockerfile
  - id: openwiki-source-16cd45085d0d6ec961432a0a
    resource: repo://BotFarm/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-e06afb7b22f39e1c736cd659
    resource: repo://BotFarm/Health/HealthReportJsonWriter.cs
  - id: openwiki-source-e03d7648563c7e6185aa4542
    resource: repo://BotFarm/Health/HealthReportLogPublisher.cs
  - id: openwiki-source-81baeca6a1a40fc83cf91310
    resource: repo://BotFarm/Health/MemoryHealthCheck.cs
  - id: openwiki-source-e5ee530d12cdb4cc0ab3552a
    resource: repo://BotFarm/HostedServices/BotControlStartupService.cs
  - id: openwiki-source-ba5787d8ab87ddac16dd9cbc
    resource: repo://BotFarm/HostedServices/NightlyBackupService.cs
  - id: openwiki-source-b413e9982e938bb3f4abe4ab
    resource: repo://BotFarm/HostedServices/PeriodicShutdownService.cs
  - id: openwiki-source-12245c66c70c78d95c593000
    resource: repo://BotFarm/Logging/LogFileTail.cs
  - id: openwiki-source-28081e19fa0768d84d8b5007
    resource: repo://BotFarm/nlog.config
  - id: openwiki-source-7b43f8d2fe7b509c09f79842
    resource: repo://docker-compose.override.yml
  - id: openwiki-source-b79fbbd921df689b4bbdc82f
    resource: repo://docker-compose.yml
  - id: openwiki-source-5e8fdcf9778b0a41b78583a2
    resource: repo://TestBot/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-2e5d2bc4f5af7a376352e2ea
    resource: repo://tests/BotFarm.UnitTests/HostedServices/NightlyBackupServiceTests.cs
  - id: openwiki-source-dfa4e728c57a4277aefefaf2
    resource: repo://tests/BotFarm.UnitTests/Logging/LogFileTailTests.cs
  - id: openwiki-source-9d620e927295728ff40c40a5
    resource: repo://tests/BotFarm.UnitTests/Logging/NLogConfigTests.cs
  - id: openwiki-source-cdcda05bbe88e211d4871ce7
    resource: repo://tests/TestBot.UnitTests/Health/TestBotStatsHealthCheckTests.cs
generated: { by: "claude-code", at: "2026-11-12T18:30:04.833Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T18:30:04.833Z
---

# Configuration and Operations

## Configuration inputs

The host uses the standard `WebApplication` configuration sources: `appsettings.json`, `appsettings.<Environment>.json`, environment variables (use `__` for `:`), and command-line arguments. Supply credentials through deployment secrets or environment variables; never commit live values or put them in this wiki.

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:MongoDb` | MongoDB connection for bot databases, the `BotFarmControl` control database, and the `BotFarmIdentity` Identity store. Required. |
| `Bots:<name>:BotConfig:Token` | Telegram bot token (`<digits>:<secret>`). Required; validated at startup. |
| `Bots:<name>:BotConfig:AdminChatId` | Chat that receives the bot's admin alerts. Required (non-zero); validated at startup. |
| `Bots:<name>:BotConfig:WebhookSecret` | Optional webhook secret token (1–256 of `A-Z a-z 0-9 _ -`). When empty, one is derived from the token. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md). |
| `Bots:<name>:BotConfig:Enabled` | Initial state, used only to seed the bot's control record the first time it runs. Afterwards the `BotFarmControl` record and the dashboard are authoritative. |
| `Bots:<name>:BotConfig:Emoji` | Optional emoji shown next to the bot in the dashboard. |
| `WebHookUrl` | Absolute HTTPS base URL, or a tunnel keyword (`devtunnel`, `localtunnel`, `ngrok`) in Development. |
| `DataPath` | Folder for runtime data (`backups`, `logs`, `tmp`, `dpkeys`). Empty means the app folder; relative paths resolve against it. The container image sets `/app/data`. |
| `Setup:Code` | Optional first-run admin setup code, at least 16 characters. When empty, a random code is generated and logged at startup. |
| `HealthChecks:ApiKey` | Optional key an uptime monitor sends as `Authorization: Bearer <key>` to read `/health`. Empty turns key access off; a non-empty key shorter than 32 characters stops startup. |
| `ScheduledJobs:BackupTime` | Nightly backup time in 24-hour `HH:mm` (default `05:00`). |
| `ScheduledJobs:TimeZone` | IANA time zone for the backup time (default `UTC`). |
| `ScheduledJobs:ShutdownEveryHours` | Optional periodic shutdown interval; zero or less disables it. |
| `ASPNETCORE_ENVIRONMENT` | Development enables the development auth handler and tunnel resolvers. |

Each bot module binds its section with `AddBotConfig`, which uses `ValidateOnStart`. A missing token, a malformed token, an invalid webhook secret, or a zero admin chat ID stops startup with a message that names the setting and never includes the token or secret. An invalid backup time or time zone also stops startup with a message naming the setting. `NightlyBackupService` is constructed when the host starts.

## Container deployment

`BotFarm/Dockerfile` is a multi-stage Alpine build. It restores from every `*.csproj` (copied with `--parents`, so newly referenced bot projects are picked up; `.dockerignore` excludes the `templates` folder so the bot template's projects are not), publishes the host (including every admin translation set under `AdminLanguages`), installs ICU (`icu-libs`, `icu-data-full`) and sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` so the admin UI's `en-US`, `ru-RU` and `uk-UA` cultures exist and dates and numbers follow the chosen language, runs as the non-root `farmer` user, listens on port `5000`, and sets `DataPath=/app/data`. The data folder is created and owned by `farmer`, so a new named volume mounted there inherits writable ownership.

The production `docker-compose.yml` maps host port `9090` to `5000`, runs in Production with the `unless-stopped` restart policy, and passes `ConnectionStrings__MongoDb`, `WebHookUrl`, an optional `Setup__Code` and an optional `HealthChecks__ApiKey` (`HEALTH_API_KEY`) from `.env` substitutions. It mounts the named volume `botfarm-data` at `/app/data`, so backups, logs, temporary files, and data-protection (sign-in) keys survive container replacement. It does not supply bot settings; provide `Bots__<name>__BotConfig__*` through a secure source.

The app itself serves plain HTTP and expects a TLS-terminating proxy (another container or a tunnel) in front of it. It trusts `X-Forwarded-For` and `X-Forwarded-Proto` from any address, because the proxy's address is not known in advance; these headers only set the request's scheme and client IP and grant no access. Outside Development the app sends HSTS and marks the sign-in cookie `Secure`, so the admin pages work only when the proxy forwards `X-Forwarded-Proto: https`. See [System Overview](../architecture/system-overview.md).

The development `docker-compose.override.yml` builds Debug and runs Development. It adds MongoDB 8.3.11 with data/config volumes and a health check, and a tunnel container that bundles localtunnel and ngrok. The app waits for a healthy MongoDB, and host `./logs` and `./backups` are mounted into `/app/data`. `WEBHOOK_MODE` (from `.env`, default `localtunnel`) selects both the tunnel client and the app's `WebHookUrl`; ngrok also needs `NGROK_AUTHTOKEN`. `.env.example` documents these variables, `SETUP_CODE` and `HEALTH_API_KEY`. Treat the override's MongoDB credentials as local-only. The override does not force globalization-invariant mode, so the app container uses the image's ICU; with invariant mode on, startup fails because the admin languages cannot be created (see [Admin UI Localization](../architecture/admin-localization.md)).

## Health checks and logs

`/healthz` is an anonymous liveness probe that runs no checks and reveals no details; the Dockerfile `HEALTHCHECK` and the production Compose health check call it with `wget`. `/health` runs every check and returns the results as JSON (`HealthReportJsonWriter`: overall status and total duration, and per check its status, description, duration, data, tags and exception message). It answers `200` when every check is healthy or degraded and `503` when any is unhealthy, so a monitor can alert on the status code. It requires the `HealthReader` policy: a signed-in admin, or a request carrying the health API key.

To let an uptime monitor read `/health`, set `HealthChecks:ApiKey` (`HEALTH_API_KEY` in `.env` for Compose) to a random secret of at least 32 characters, for example from `openssl rand -hex 32`. The monitor sends it as `Authorization: Bearer <key>`. `HealthApiKeyAuthenticationHandler` compares SHA-256 hashes of the sent and configured keys in constant time. With no key configured or no bearer header it does not authenticate; a wrong key fails. A signed-out request to `/health` gets `401` instead of the admin cookie's redirect to the sign-in page, so a monitor with a bad key never sees the login page as "up". The key's identity satisfies only the `HealthReader` policy, so it opens no admin page. Send it over HTTPS only and never in the URL. Admins see the same results on the `/dashboard/health` page; see [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

A health-check publisher runs every registered check every five minutes; `HealthReportLogPublisher` logs a healthy result at Information level and otherwise logs a warning naming each failing check. That keeps checks with side effects running when nobody reads `/health`. The health checks report process memory and uptime. The memory check reports the process working set (physical memory in use), the GC heap size measured at the last collection (which shows steady growth without uncollected garbage), and the collection counts per generation. A working set at or above `MemoryCheckOptions.Threshold` (default about 400 MB) reports unhealthy and sends a warning notification. Each bot module can add checks; TestBot reports its chat count and is unhealthy if the query fails.

NLog writes Information-and-higher logs to the console and to `nlog-all.log` and `nlog-own.log` in `<DataPath>/logs` (or the app folder's `logs` when `DataPath` is empty). The files archive daily, keeping up to seven archives. The dashboard's log viewer reads these files through `LogFileTail`, which groups continuation lines (such as stack traces) with their entry, filters by level, and, for a file still being written, follows appended lines; it consumes only complete lines and starts over when the file shrinks after a roll-over. `NLogConfigTests` verify the log folder for absolute and relative data paths. `LogFileTailTests` cover entry grouping, level parsing, tailing and roll-over.

## Scheduled work and shutdown

Scheduled work runs as hosted services:

- `NightlyBackupService` computes the next run in the configured time zone and logs it. Its `GetNextRun` handles daylight-saving transitions: a time skipped when clocks move forward runs just after the jump, and a repeated time runs only once. At each run it backs up every bot module in turn. A failed `Result` is logged as a warning (the backup service already alerted). An exception is logged and alerted, and the remaining bots still run. Stopping the host cancels a running backup. The process is not restarted after backups.
- `PeriodicShutdownService` stops the application once after `ShutdownEveryHours` hours when the value is positive, so an external supervisor (such as Compose `unless-stopped`) can restart it.
- `BotControlStartupService` applies each bot's saved enabled state after startup. A bot whose transition fails stays gated in `Unknown` or `Error` while the host stays available for an operator retry; see [Bot Runtime Lifecycle](../architecture/bot-runtime.md).

The host allows 25 seconds for graceful shutdown. Archive format, restore, and retention are in [Persistence and Backups](../architecture/persistence-and-backups.md). Bot archives cover each bot database, not `BotFarmControl` or `BotFarmIdentity`. Include those in any MongoDB-level backup that must preserve operator-selected bot state and accounts.

## Continuous integration

`.github/workflows/ci.yml` runs on pushes to `master`, pull requests, and manual dispatch. It uses .NET 10 on Ubuntu, restores, builds `BotFarm.sln` in Release, and runs `dotnet test` with a 15-minute timeout. Concurrent runs for the same ref are cancelled. See [Testing Strategy](../testing/strategy.md).
