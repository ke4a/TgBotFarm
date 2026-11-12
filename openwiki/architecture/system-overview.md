---
type: architecture
title: System Overview
description: Maps the BotFarm web host, reusable libraries, and bot modules, then traces how the minimal-hosting entry point composes services, discovers bots at build time, runs hosted services, and stores runtime data.
tags: [architecture, hosting, dotnet]
sources:
  - id: openwiki-source-715dace563ef484b6e8bd1e2
    resource: repo://.dockerignore
  - id: openwiki-source-4441e85b1598ce0d171b3f1d
    resource: repo://BotFarm.Core/Abstractions/INotificationService.cs
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-a8ad2bd5db955bd1890a707c
    resource: repo://BotFarm.Core/Health/BotStatsHealthCheck.cs
  - id: openwiki-source-554d6aed4058b870daf58f39
    resource: repo://BotFarm.Core/Services/AppDataPaths.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-69c16ef7b489cc9e0c8f29d0
    resource: repo://BotFarm.Core/Services/BotModuleDiscovery.cs
  - id: openwiki-source-c431b57e452c6e5737ea444d
    resource: repo://BotFarm.Core/Services/TelegramNotificationService.cs
  - id: openwiki-source-ec955587fda830e5b684cc34
    resource: repo://BotFarm.Shared/BotFarm.Shared.csproj
  - id: openwiki-source-1ec73b08b7d229c529b32a43
    resource: repo://BotFarm/BotFarm.csproj
  - id: openwiki-source-16cd45085d0d6ec961432a0a
    resource: repo://BotFarm/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-e03d7648563c7e6185aa4542
    resource: repo://BotFarm/Health/HealthReportLogPublisher.cs
  - id: openwiki-source-e5ee530d12cdb4cc0ab3552a
    resource: repo://BotFarm/HostedServices/BotControlStartupService.cs
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
  - id: openwiki-source-6f14174db8a735fbc4fe0e7d
    resource: repo://templates/BotFarm.Bot/.template.config/template.json
  - id: openwiki-source-5e8fdcf9778b0a41b78583a2
    resource: repo://TestBot/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5ce60497ecc30fcc90af7c32
    resource: repo://TestBot/TestBot.csproj
  - id: openwiki-source-e900680f9acc645634a5c102
    resource: repo://tests/BotFarm.UnitTests/WebApplicationExtensionsTests.cs
generated: { by: "claude-code", at: "2026-11-12T18:30:04.833Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T18:30:04.833Z
---

# System Overview

BotFarm is a .NET 10 ASP.NET Core application that hosts Telegram bots alongside an authenticated Blazor/Razor operator interface. Reusable bot, webhook, and persistence infrastructure lives in libraries. Each bot is a separate project that plugs into the host as a bot module; `TestBot` is the reference bot.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `BotFarm` | Executable web host: composition, authentication, dashboard API, Razor Pages, Blazor dashboard, health endpoints, and hosted services. |
| `BotFarm.Core` | Bot contracts and base classes (`BotService`, `UpdateService`, `MongoDbDatabaseService`, `UpdateControllerBase`), bot-module discovery, bot control, webhooks, MongoDB, bot-message localization, the admin UI string localizer and language catalog, notifications, broadcasts, and backups. |
| `BotFarm.Shared` | Reusable Blazor dashboard components, the main layout with the language picker, the MudBlazor localizer, the shared admin translation set, and formatting utilities; references `BotFarm.Core`. |
| `TestBot` | Reference bot module: its keyed services, update controller, handlers, database service, and dashboard page. A Razor class library, not an executable. |
| `tests/*` | NUnit test projects for host, core, shared UI, and TestBot behavior, plus `BotFarm.TestKit` helpers. |
| `templates/BotFarm.Bot` | `dotnet new` template (`botfarm-bot`) for a new bot project and its unit test project. Not part of the solution, and excluded from the Docker build context. |
| `scripts/new-bot.sh`, `scripts/new-bot.ps1` | Scaffold a bot from the template and wire it into the solution, the host, and the settings files; see [Adding a Bot Module](../workflows/adding-a-bot.md). |

The host references Core, Shared, and TestBot; TestBot references Core and Shared. Package versions are managed centrally in `Directory.Packages.props`.

Admin alerts (`INotificationService.SendErrorNotification` and `SendWarningNotification`) are best-effort: `TelegramNotificationService` logs a failed send as a warning instead of throwing, so callers report errors without guarding the alert itself. Sending an operator message to a chat (`SendMessage`) still throws on failure.

## Composition

`Program.Main` records the start time, creates a `WebApplicationBuilder`, configures NLog and a 25-second shutdown timeout, calls `builder.AddBotFarm()`, builds the app, calls `app.UseBotFarm()`, and runs it. Configuration comes from the standard `WebApplication` host sources (JSON settings files, environment variables, command line), not a custom builder.

`AddBotFarm` (in `BotFarm/Extensions/WebApplicationExtensions.cs`) registers:

- forwarded-headers options that accept `X-Forwarded-For` and `X-Forwarded-Proto` from any address (the TLS proxy's address is not known in advance, and these headers only set the request's scheme and client IP, which grant no access), data protection keys persisted to the data folder, MVC controllers with Telegram's MVC integration, Razor Pages (everything authorized except `/Account`), an HTML encoder that leaves Cyrillic and typographic punctuation unescaped, interactive server Razor components, and MudBlazor with `AdminMudLocalizer` as its localizer;
- the setup-code service and the hosted services: setup announcement, bot-control startup, nightly backup, and periodic shutdown;
- `AddCoreServices`, then `AddAdminLocalization` (see [Admin UI Localization](./admin-localization.md)), then every discovered bot module;
- host and bot health checks with a health-check publisher that runs every check every five minutes and logs the result (`HealthReportLogPublisher`), MongoDB-backed Identity with `LocalizedIdentityErrorDescriber`, and authentication: the admin scheme (the Identity cookie, or the development scheme in Development) as the default, plus the optional health API key scheme and the `HealthReader` policy that accepts either (see [Configuration and Operations](../operations/deployment.md)). The sign-in cookie lasts 14 days with sliding expiration and is marked `Secure` outside Development, where the admin pages are reached only through the HTTPS proxy; in Development it follows the request's scheme.

`AddCoreServices` provides `HybridCache`, HTTP clients, `TimeProvider.System`, the `BotConfig` validator, `IAppDataPaths`, localization, the Telegram client factory, the webhook secret provider, the update delivery guard, one singleton `MongoClient` from `ConnectionStrings:MongoDb`, the bot registry, notifications, the singleton `IBroadcastService` (one running broadcast per bot across dashboard sessions), backup services, webhook URL resolvers, the webhook initializer, the control-state store, and the singleton `BotControlCoordinator`, which is also exposed as `IBotControlService`. Tunnel webhook resolvers are registered only in Development, ahead of the static fallback resolver; see [Telegram Webhook Integration](../integrations/telegram-webhooks.md).

## Bot modules and keyed services

Bots are found at startup, not hard-coded. `BotModuleDiscovery.Discover(typeof(Program).Assembly)` reads the `ApplicationPartAttribute`s that the build places on the host assembly for every referenced project with MVC controllers. It loads those assemblies and creates the `IBotModule` that each one declares with `[assembly: BotModule(typeof(...))]`. Modules are ordered by name, and two modules with the same name (ignoring case) stop startup.

For each module, the host registers the module as an `IBotModule` singleton and a `BotIdentity` singleton keyed by the module name. It then lets the module add its own services and health checks. Per-bot services are keyed by bot name. `IBotRegistry` resolves them by name and enumerates them in module order, using the registered modules as the list of bots. `UseBotFarm` logs the loaded module names and adds each module's components assembly to the Razor component routes, so bot pages such as `/testbot/dashboard` are routable. How to write a module is covered in [Adding a Bot Module](../workflows/adding-a-bot.md).

## Request pipeline and endpoints

`UseBotFarm` first resolves the admin language catalog and logs the loaded languages, so broken translation files stop startup. It then applies forwarded headers, so the scheme and client IP come from the TLS-terminating proxy, adds HSTS outside Development, and adds static files, routing, admin localization (request culture and the language cookie, skipped for `/api`, `/health` and `/healthz`), authentication, authorization, and antiforgery. It maps Razor Pages, controllers (including each bot's update controller), an anonymous `/healthz` liveness probe that runs no checks, `/health` (every check's result as JSON) behind the `HealthReader` policy, the anonymous `/culture/set` language switch, and the Razor components with authorization required. The Blazor health page `/dashboard/health` is one of those authorized components and runs the checks on demand. Host health checks report memory and uptime. Each bot module can add its own; TestBot adds the Core `BotStatsHealthCheck`, which reports the chat count and is unhealthy when the query fails. The operator surfaces are described in [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Lifecycle and runtime data

Bot startup runs in `BotControlStartupService`, which waits until the application has started and then initializes each bot's saved enabled state. Until that succeeds, each bot's update gate stays closed because its runtime status begins `Unknown`. See [Bot Runtime Lifecycle](./bot-runtime.md). The nightly backup and optional periodic shutdown are separate hosted services; see [Configuration and Operations](../operations/deployment.md).

Runtime files live under one data folder resolved by `AppDataPaths` from the `DataPath` setting. An empty value means the app folder, and a relative path is resolved against it. The folder holds `backups`, `logs`, `tmp`, and `dpkeys` (data-protection keys). Consumers create their own subfolder on first use. NLog reads the same setting for its log directory.

## Focused tests

`WebApplicationExtensionsTests` verify that `AddBotFarm` uses host-provided configuration, registers the referenced bot modules with identities keyed by name, registers TestBot's core services keyed only, marks the sign-in cookie secure in production, and accepts the forwarded scheme and client IP from any proxy; they also check that discovery from the host assembly finds the TestBot module. `BotModuleDiscoveryTests` and `BotRegistryTests` cover module validation, ordering, duplicate names, and keyed lookups. `CoreServicesDependencyInjectionTests` verify the singleton Mongo client and the failure when its connection string is missing.
