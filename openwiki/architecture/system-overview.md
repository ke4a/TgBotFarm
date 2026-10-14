---
type: architecture
title: System Overview
description: Maps the BotFarm web host, reusable libraries, and reference bot, then traces how services are composed and how the process starts and shuts down.
tags: [architecture, hosting, dotnet]
sources:
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-a8ad2bd5db955bd1890a707c
    resource: repo://BotFarm.Core/Health/BotStatsHealthCheck.cs
  - id: openwiki-source-ef49977dd991e45175722186
    resource: repo://BotFarm.Core/Health/BotStatsHealthCheckBuilderExtensions.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-ec955587fda830e5b684cc34
    resource: repo://BotFarm.Shared/BotFarm.Shared.csproj
  - id: openwiki-source-1ec73b08b7d229c529b32a43
    resource: repo://BotFarm/BotFarm.csproj
  - id: openwiki-source-554ff6a3a821fe0433ae1b15
    resource: repo://BotFarm/HostedServices/DatabaseShutdownHostedService.cs
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
  - id: openwiki-source-5a967c2a70e92f59eae7aebf
    resource: repo://BotFarm/Startup.cs
  - id: openwiki-source-5e8fdcf9778b0a41b78583a2
    resource: repo://TestBot/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5ce60497ecc30fcc90af7c32
    resource: repo://TestBot/TestBot.csproj
  - id: openwiki-source-cdcda05bbe88e211d4871ce7
    resource: repo://tests/TestBot.UnitTests/Health/TestBotStatsHealthCheckTests.cs
generated: { by: "copilot", at: "2026-10-14T09:31:37.432Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-10-14T09:31:37.432Z
---

# System Overview

BotFarm is a .NET 10 ASP.NET Core application that hosts bot services alongside an authenticated Blazor/Razor operator interface. The solution separates reusable bot and persistence infrastructure from the host UI, while the `TestBot` project supplies a concrete bot implementation registered into the host.

## Project boundaries

| Project | Responsibility |
| --- | --- |
| `BotFarm` | Executable web host, authentication, controllers, Razor Pages, Blazor dashboard, health endpoints, scheduled jobs, and host lifecycle. |
| `BotFarm.Core` | Shared bot contracts and services for Telegram clients, MongoDB, localization, notifications, webhooks, and backup operations. |
| `BotFarm.Shared` | Reusable Blazor dashboard components and presentation utilities; it references `BotFarm.Core`. |
| `TestBot` | Reference bot implementation, including its keyed service registrations, update endpoint, handlers, and database extension. It is a Razor class library, not a separate executable. |
| `tests/*` | NUnit test projects for host, core, shared UI, and TestBot behavior, plus `BotFarm.TestKit` helpers. |

The web host references Core, Shared, and TestBot. TestBot references Core and Shared, so its services and controller are composed into the same application. Package versions are managed centrally in `Directory.Packages.props`.

## Service composition

`Startup.ConfigureServices` registers the web surfaces and host infrastructure, then calls `AddCoreServices` and `AddTestBotServices`. Core registration provides the hybrid cache, HTTP clients, localization, a Telegram client factory, one singleton Mongo client created from `ConnectionStrings:MongoDb`, the bot registry, notifications, backup services, webhook initialization, and the singleton bot-control coordinator and state store. The coordinator is also exposed as `IBotControlService`, so the dashboard, update processors, and host use the same process-local control status. TestBot registration binds its named configuration and supplies the bot-specific services.

Webhook URL resolvers are ordered registrations: development builds include tunnel resolvers before the static resolver, which is the fallback. For webhook selection and update processing, see [Telegram Webhook Integration](../integrations/telegram-webhooks.md) and [Bot Runtime and Update Processing](./bot-runtime.md).

The host also configures MongoDB-backed Identity, cookie or development authentication, protected health endpoints, MVC controllers, Razor Pages, and interactive server-side Blazor. `ConfigureHealthChecks` registers host memory and application-stat checks; `AddTestBotHealthChecks` adds the Core `BotStatsHealthCheck` for TestBot's database service. It reports the chat count as health data and returns an unhealthy result if the count query fails. The operator experience is described in [Operator Dashboard and Administration](../workflows/operator-dashboard.md); database ownership and backup behavior are covered in [Persistence and Backups](./persistence-and-backups.md).

## Process lifecycle

`Program.Main` records the application start time and builds the host. It constructs and starts scheduled jobs before starting the web host. Once the host is started, it initializes bot control state for each registered bot; webhook transitions then follow that authoritative desired state. Until initialization succeeds, each bot's inbound update gate remains closed. If shutdown cancels initialization, the program waits for host shutdown instead of continuing startup. The web host uses NLog and sets a 25-second shutdown timeout.

`DatabaseShutdownHostedService` has no startup work. During graceful shutdown it iterates the registered `IDatabaseService` instances, calls `Disconnect`, and logs that the hosting environment initiated shutdown. Scheduled backup and restart behavior is documented in [Configuration and Operations](../operations/deployment.md).

The Core registration tests verify that `IMongoClient` is shared as a singleton and that resolving it without a MongoDB connection string throws. TestBot health-check tests verify both the healthy chat-count result and an unhealthy result when its database query fails.

## Configuration boundary

`Startup` builds application configuration from `appsettings.json`, the environment-specific JSON file when present, and environment variables. Named bot options are bound by the bot registration extensions; the MongoDB connection and other host settings are consumed by the services that own them. See [Configuration and Operations](../operations/deployment.md) for the operational key map. No credential values belong in this wiki.
