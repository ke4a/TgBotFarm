---
type: architecture
title: System Overview
description: Maps the BotFarm web host, reusable libraries, and bot modules, then traces how the minimal-hosting entry point composes services, discovers bots at build time, runs hosted services, and stores runtime data.
tags: [architecture, hosting, dotnet]
verified:
  - by: openwiki/0.7.0
    at: 2026-11-05T14:58:19.382Z
sources:
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
  - id: openwiki-source-ec955587fda830e5b684cc34
    resource: repo://BotFarm.Shared/BotFarm.Shared.csproj
  - id: openwiki-source-1ec73b08b7d229c529b32a43
    resource: repo://BotFarm/BotFarm.csproj
  - id: openwiki-source-16cd45085d0d6ec961432a0a
    resource: repo://BotFarm/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-e5ee530d12cdb4cc0ab3552a
    resource: repo://BotFarm/HostedServices/BotControlStartupService.cs
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
  - id: openwiki-source-5e8fdcf9778b0a41b78583a2
    resource: repo://TestBot/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5ce60497ecc30fcc90af7c32
    resource: repo://TestBot/TestBot.csproj
  - id: openwiki-source-e900680f9acc645634a5c102
    resource: repo://tests/BotFarm.UnitTests/WebApplicationExtensionsTests.cs
generated: { by: "claude-code", at: "2026-11-05T14:58:19.382Z" }
---

# System Overview

BotFarm is a .NET 10 ASP.NET Core application that hosts Telegram bots alongside an authenticated Blazor/Razor operator interface. Reusable bot, webhook, and persistence infrastructure lives in libraries. Each bot is a separate project that plugs into the host as a bot module; `TestBot` is the reference bot.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `BotFarm` | Executable web host: composition, authentication, dashboard API, Razor Pages, Blazor dashboard, health endpoints, and hosted services. |
| `BotFarm.Core` | Bot contracts and base classes (`BotService`, `UpdateService`, `MongoDbDatabaseService`, `UpdateControllerBase`), bot-module discovery, bot control, webhooks, MongoDB, localization, notifications, and backups. |
| `BotFarm.Shared` | Reusable Blazor dashboard components, the main layout, and formatting utilities; references `BotFarm.Core`. |
| `TestBot` | Reference bot module: its keyed services, update controller, handlers, database service, and dashboard page. A Razor class library, not an executable. |
| `tests/*` | NUnit test projects for host, core, shared UI, and TestBot behavior, plus `BotFarm.TestKit` helpers. |

The host references Core, Shared, and TestBot; TestBot references Core and Shared. Package versions are managed centrally in `Directory.Packages.props`.

## Composition

`Program.Main` records the start time, creates a `WebApplicationBuilder`, configures NLog and a 25-second shutdown timeout, calls `builder.AddBotFarm()`, builds the app, calls `app.UseBotFarm()`, and runs it. Configuration comes from the standard `WebApplication` host sources (JSON settings files, environment variables, command line), not a custom builder.

`AddBotFarm` (in `BotFarm/Extensions/WebApplicationExtensions.cs`) registers:

- data protection keys persisted to the data folder, MVC controllers with Telegram's MVC integration, Razor Pages (everything authorized except `/Account`), interactive server Razor components, and MudBlazor;
- the setup-code service and the hosted services: setup announcement, bot-control startup, nightly backup, and periodic shutdown;
- `AddCoreServices`, then every discovered bot module;
- host and bot health checks, MongoDB-backed Identity, the health-check authorization policy, and authentication schemes.

`AddCoreServices` provides `HybridCache`, HTTP clients, `TimeProvider.System`, the `BotConfig` validator, `IAppDataPaths`, localization, the Telegram client factory, the webhook secret provider, the update delivery guard, one singleton `MongoClient` from `ConnectionStrings:MongoDb`, the bot registry, notifications, backup services, webhook URL resolvers, the webhook initializer, the control-state store, and the singleton `BotControlCoordinator`, which is also exposed as `IBotControlService`. Tunnel webhook resolvers are registered only in Development, ahead of the static fallback resolver; see [Telegram Webhook Integration](../integrations/telegram-webhooks.md).

## Bot modules and keyed services

Bots are found at startup, not hard-coded. `BotModuleDiscovery.Discover(typeof(Program).Assembly)` reads the `ApplicationPartAttribute`s that the build places on the host assembly for every referenced project with MVC controllers. It loads those assemblies and creates the `IBotModule` that each one declares with `[assembly: BotModule(typeof(...))]`. Modules are ordered by name, and two modules with the same name (ignoring case) stop startup.

For each module, the host registers the module as an `IBotModule` singleton and a `BotIdentity` singleton keyed by the module name. It then lets the module add its own services and health checks. Per-bot services are keyed by bot name. `IBotRegistry` resolves them by name and enumerates them in module order, using the registered modules as the list of bots. `UseBotFarm` logs the loaded module names and adds each module's components assembly to the Razor component routes, so bot pages such as `/testbot/dashboard` are routable. How to write a module is covered in [Adding a Bot Module](../workflows/adding-a-bot.md).

## Request pipeline and endpoints

`UseBotFarm` adds static files, routing, authentication, authorization, and antiforgery, then maps Razor Pages, controllers (including each bot's update controller), an anonymous `/healthz` liveness probe that runs no checks, `/health` and `/health-ui` behind the health-check policy, and the Razor components with authorization required. Host health checks report memory and uptime. Each bot module can add its own; TestBot adds the Core `BotStatsHealthCheck`, which reports the chat count and is unhealthy when the query fails. The operator surfaces are described in [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Lifecycle and runtime data

Bot startup runs in `BotControlStartupService`, which waits until the application has started and then initializes each bot's saved enabled state. Until that succeeds, each bot's update gate stays closed because its runtime status begins `Unknown`. See [Bot Runtime Lifecycle](./bot-runtime.md). The nightly backup and optional periodic shutdown are separate hosted services; see [Configuration and Operations](../operations/deployment.md).

Runtime files live under one data folder resolved by `AppDataPaths` from the `DataPath` setting. An empty value means the app folder, and a relative path is resolved against it. The folder holds `backups`, `logs`, `tmp`, and `dpkeys` (data-protection keys). Consumers create their own subfolder on first use. NLog reads the same setting for its log directory.

## Focused tests

`WebApplicationExtensionsTests` verify that `AddBotFarm` uses host-provided configuration, registers the referenced bot modules with identities keyed by name, and registers TestBot's core services keyed only; they also check that discovery from the host assembly finds the TestBot module. `BotModuleDiscoveryTests` and `BotRegistryTests` cover module validation, ordering, duplicate names, and keyed lookups. `CoreServicesDependencyInjectionTests` verify the singleton Mongo client and the failure when its connection string is missing.
