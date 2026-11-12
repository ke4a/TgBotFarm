---
type: guide
title: Quickstart
description: Orients contributors to the .NET solution, the settings a local run needs and how they are validated, run and test commands, the first-run setup code, and the wiki pages for deeper system guidance.
tags: [quickstart, development, dotnet]
sources:
  - id: openwiki-source-5f5b95b3d6a215fa02ceb945
    resource: repo://.env.example
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-21cedf68ac5206cbb456e09a
    resource: repo://BotFarm.Core/Models/BotConfig.cs
  - id: openwiki-source-06c01f5e5d0d64d6d046c258
    resource: repo://BotFarm.Core/Services/BotConfigValidator.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-a06f6c23ee056cc162bc8d5a
    resource: repo://BotFarm.Core/Services/MongoBotControlStateStore.cs
  - id: openwiki-source-5328901543edbdcc02b894c1
    resource: repo://BotFarm.Shared/MainLayout.razor
  - id: openwiki-source-9ea84f99b1a810626d82a3a4
    resource: repo://BotFarm.sln
  - id: openwiki-source-1ec73b08b7d229c529b32a43
    resource: repo://BotFarm/BotFarm.csproj
  - id: openwiki-source-8e7e36dadb3658bf71310d7d
    resource: repo://BotFarm/Dockerfile
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-8f7039ecdaca4e666f599833
    resource: repo://BotFarm/HostedServices/SetupAnnouncementHostedService.cs
  - id: openwiki-source-eb904619a071eca6885b5c45
    resource: repo://BotFarm/Pages/Account/_LanguagePicker.cshtml
  - id: openwiki-source-d7a307bf5764d5ffcff4415d
    resource: repo://BotFarm/Pages/Account/Login.cshtml.cs
  - id: openwiki-source-54f57d8ffb92cfc1015f6a01
    resource: repo://BotFarm/Pages/Account/Setup.cshtml.cs
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
  - id: openwiki-source-7b43f8d2fe7b509c09f79842
    resource: repo://docker-compose.override.yml
  - id: openwiki-source-b79fbbd921df689b4bbdc82f
    resource: repo://docker-compose.yml
  - id: openwiki-source-23775c3de52f3ab95a13cb8b
    resource: repo://README.md
  - id: openwiki-source-b44ec18a3b072cd326f72a45
    resource: repo://scripts/new-bot.sh
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
generated: { by: "claude-code", at: "2026-11-12T18:30:04.833Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T18:30:04.833Z
---

# Quickstart

BotFarm is a .NET 10 solution. `BotFarm.sln` contains the web host (`BotFarm`), the reusable libraries (`BotFarm.Core`, `BotFarm.Shared`), the reference bot module (`TestBot`), and four NUnit test projects (two also use bUnit for Blazor component rendering) plus `BotFarm.TestKit`. The host needs MongoDB and at least one configured bot. Keep credentials in environment variables, user secrets, or `.env`, never in committed files.

## Configure a local run

The host uses the standard `WebApplication` configuration sources (JSON settings, environment variables, command line). Provide:

- `ConnectionStrings:MongoDb` — required.
- `Bots:TestBot:BotConfig:Token` and `Bots:TestBot:BotConfig:AdminChatId` — required and validated at startup. A missing or malformed token, an invalid `WebhookSecret`, or a zero admin chat ID stops the app with a message naming the setting.
- `Bots:TestBot:BotConfig:Enabled` — the initial state, used only the first time the bot runs. After that, the saved state in the `BotFarmControl` MongoDB database and the dashboard switch are authoritative.
- `WebHookUrl` — an absolute HTTPS base URL, or a tunnel keyword in Development. See [Telegram Webhook Integration](./integrations/telegram-webhooks.md).
- Optional: `DataPath` (runtime data folder), `Setup:Code` (fixed first-run setup code, at least 16 characters), and `ScheduledJobs:*`. See [Configuration and Operations](./operations/deployment.md) for the full key map.

Then run the host:

```sh
dotnet run --project BotFarm/BotFarm.csproj
```

With Docker Compose locally, `docker-compose.override.yml` adds MongoDB 8.3.11 and a webhook tunnel (localtunnel by default; set `WEBHOOK_MODE=ngrok` and `NGROK_AUTHTOKEN` in `.env` for ngrok; see `.env.example`). It also mounts host `logs` and `backups` folders into the app's `/app/data`. Production uses `docker-compose.yml` alone, which persists `/app/data` in a named volume and needs externally supplied MongoDB, webhook, and bot settings, plus a TLS-terminating proxy in front that sends `X-Forwarded-Proto: https` (the sign-in cookie is HTTPS-only outside Development). Setting `HEALTH_API_KEY` (at least 32 characters) lets an uptime monitor read `/health` with `Authorization: Bearer <key>`.

## Build and test

From the repository root:

```sh
dotnet build BotFarm.sln
```

```sh
dotnet test BotFarm.sln
```

To run one test project:

```sh
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj
```

CI runs the same build and tests in Release on every push to `master` and on every pull request. See [Testing Strategy](./testing/strategy.md).

## Create a new bot

Scaffold a bot from the `templates/BotFarm.Bot` template (Bash needs `dotnet` and `jq`; the optional second argument is the dashboard emoji):

```sh
./scripts/new-bot.sh WeatherBot
```

On any OS with PowerShell 7+:

```sh
./scripts/new-bot.ps1 WeatherBot
```

The script creates the bot and its test project, adds them to the solution, references the bot from the host, adds a disabled `Bots:WeatherBot` section to the `BotFarm/appsettings*.json` files, and builds. Then set the bot's token and admin chat ID, enable it, and add handlers. See [Adding a Bot Module](./workflows/adding-a-bot.md).

## First dashboard visit

In Production, while no account exists, `/Account/Login` redirects to `/Account/Setup`. Creating the first admin requires the setup code. That is the value of `Setup:Code` if you set it, otherwise a random code printed as a warning in the startup log, regenerated on every restart. After setup, use the login page. Development signs you in with a synthetic identity. The admin UI starts in the browser's language when it is English, Russian or Ukrainian (otherwise English), and the language links on the account pages or the translate icon in the app bar switch it. The home page shows a night-city scene where each bot drives by as its emoji (click one to open its page). The drawer links to the health page (`/dashboard/health`), and the dashboard shows host memory, uptime and logs, bot lifecycle controls, and each bot's page, where you manage backups, chats (send, send to all, delete, clean up), the webhook, and stats; see [Operator Dashboard and Administration](./workflows/operator-dashboard.md).

## Wiki map

- [System Overview](./architecture/system-overview.md) — project boundaries, `AddBotFarm`/`UseBotFarm` composition, bot-module discovery, keyed services, and the data folder.
- [Adding a Bot Module](./workflows/adding-a-bot.md) — scaffolding a bot with `scripts/new-bot.sh`/`new-bot.ps1`, the module contract the host expects, and storing bot data, with TestBot as the reference.
- [Bot Runtime Lifecycle](./architecture/bot-runtime.md) — durable desired state, transitions and retries, maintenance and shutdown pauses, the update gate, and update routing, including supergroup chat migration.
- [Telegram Webhook Integration](./integrations/telegram-webhooks.md) — URL resolvers, the webhook secret, delivery handling, duplicates, retries, dropping pending updates, broadcasts, and chat reachability.
- [Persistence and Backups](./architecture/persistence-and-backups.md) — MongoDB ownership, control-state storage, chat settings, bot data layout, chat migration and deletion, and archive behavior including index backup and restore.
- [Admin UI Localization](./architecture/admin-localization.md) — translation sets under `AdminLanguages`, how the request language is chosen and stored, the language switch, MudBlazor and Identity texts, ICU in containers, and adding a bot's set.
- [Configuration and Operations](./operations/deployment.md) — settings and validation, containers (including ICU for the admin languages), health probes and the `/health` API key for uptime monitors, scheduled services, logs, and CI.
- [Operator Dashboard and Administration](./workflows/operator-dashboard.md) — setup code, sign-in and sign-out, the home page scene, the layout, theme and language picker, live uptime, bot dashboards (backups, chats, broadcasts, chat clean-up, webhook card, stats), the log viewer, shutdown, bot controls, the health page, and who can read `/health`.
- [Spec-Kit Feature Workflow](./workflows/spec-kit.md) — Claude Code skills and the specification-to-implementation artifact flow.
- [Testing Strategy](./testing/strategy.md) — test-project map, TestKit helpers, and focused coverage.
