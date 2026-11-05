---
type: workflow
title: Adding a Bot Module
description: Step-by-step guide to plugging a new Telegram bot project into the BotFarm host as a bot module, covering discovery, naming, validated configuration, the keyed services the host expects, the update controller, health checks, pages, and tests, with TestBot as the reference.
tags: [workflow, bots, modules, extension]
verified:
  - by: openwiki/0.7.0
    at: 2026-11-05T14:58:19.382Z
sources:
  - id: openwiki-source-4236d4bb95f416e69dc8cc45
    resource: repo://BotFarm.Core/Abstractions/IBotModule.cs
  - id: openwiki-source-e001cba704267f13f505ac68
    resource: repo://BotFarm.Core/Abstractions/IBotRegistry.cs
  - id: openwiki-source-d150e8d260bda8b7c7d64ae2
    resource: repo://BotFarm.Core/Base/BotModuleAttribute.cs
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-06c01f5e5d0d64d6d046c258
    resource: repo://BotFarm.Core/Services/BotConfigValidator.cs
  - id: openwiki-source-69c16ef7b489cc9e0c8f29d0
    resource: repo://BotFarm.Core/Services/BotModuleDiscovery.cs
  - id: openwiki-source-946d75c27177bf361d033b02
    resource: repo://BotFarm.Core/Services/BotRegistry.cs
  - id: openwiki-source-1d926748a20c3e7b9c558fe5
    resource: repo://BotFarm.Core/Services/BotWebhookInitializerService.cs
  - id: openwiki-source-32d040660ba266e85195778b
    resource: repo://BotFarm.Core/Services/JsonLocalizationService.cs
  - id: openwiki-source-5328901543edbdcc02b894c1
    resource: repo://BotFarm.Shared/MainLayout.razor
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-254c9b517d5aa3409beaff97
    resource: repo://TestBot/Controllers/UpdateController.cs
  - id: openwiki-source-b273e7de5ce8bb6689e05057
    resource: repo://TestBot/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-66cfed0bff7015b74aadf547
    resource: repo://TestBot/Pages/TestBotDashboard.razor
  - id: openwiki-source-f9f5e32d1481d29435f8e1e0
    resource: repo://TestBot/TestBotModule.cs
generated: { by: "claude-code", at: "2026-11-05T14:58:19.382Z" }
---

# Adding a Bot Module

A bot is a separate Razor class library that the host finds when it starts. The library contributes one `IBotModule`; the host never names bots in code. `TestBot` is the reference implementation, so copying its layout is the fastest start.

## How the host finds a module

1. **Reference the project from the host.** Add a `ProjectReference` to `BotFarm/BotFarm.csproj`. The Dockerfile copies every `*.csproj` with `--parents`, so container builds pick up the new project without changes.
2. **Have an MVC controller.** At build time, the SDK writes an `ApplicationPartAttribute` onto the host assembly for each referenced project that contains controllers. `BotModuleDiscovery.Discover` reads those attributes, so the bot's update controller is what puts the project on the list.
3. **Declare the module.** Exactly one `[assembly: BotModule(typeof(MyBotModule))]` per project. The type must implement `IBotModule`, be concrete and non-generic, and have a public parameterless constructor. Otherwise startup fails with a message that names the type and the declaring assembly.
4. **Pick a unique name.** `IBotModule.Name` is the bot's identity everywhere: its configuration section, the DI key for its services, its database name, and its webhook route. Modules are ordered by name. Two names that match ignoring case stop startup with both type names in the error.

For each module, the host registers the module as an `IBotModule` singleton and a `BotIdentity(Name)` singleton keyed by the name. It then calls `AddServices(services, configuration)` and `AddHealthChecks(healthChecks)`. In the request pipeline it adds the module's `ComponentsAssembly` (by default the module's own assembly) to the Razor component routes. See [System Overview](../architecture/system-overview.md).

## Configuration

Call `services.AddBotConfig(Name, configuration)` in `AddServices`. It binds `Bots:<Name>:BotConfig` as named `BotConfig` options and validates them when the host starts:

| Key | Rule |
| --- | --- |
| `Token` | Required; must look like `<digits>:<secret>`. |
| `AdminChatId` | Required (non-zero); receives the bot's admin alerts. |
| `WebhookSecret` | Optional; 1–256 of `A-Z a-z 0-9 _ -`. When empty, a secret is derived from the token. |
| `Enabled` | Initial state, used only the first time the bot runs; afterwards the dashboard controls it. |
| `Emoji` | Optional; shown next to the bot name in the dashboard. |

Validation messages name the setting path and never include the token or secret. Real values belong in environment variables or secrets (`Bots__<Name>__BotConfig__Token`), not in committed JSON. See [Configuration and Operations](../operations/deployment.md).

## Services the host expects

Register bot-scoped services **keyed by the bot name**. The host's coordinator, backups, dashboards, and notifications resolve them through `IBotRegistry`, and `GetServicesForAllBots` throws if any bot lacks one. TestBot registers:

- `IBotService` → a class deriving from `BotService`, keyed singleton. It owns the Telegram client and webhook calls. `GetMe` runs in `Initialize`, and the bot-specific setup goes in an override.
- `IUpdateService` → a class deriving from `UpdateService<TChatSettings>`, keyed scoped. The base class already routes `/start`, `/changelanguage`, language callbacks, and the bot-joined welcome. Override `OnUpdate` for the bot's own behavior, or `ProcessUpdate` for entirely different routing.
- `ICommandHandler` / `ICallbackHandler` implementations, keyed scoped. They are injected into the update service with `[FromKeyedServices(name)]` and dispatched by their `Command` or `CallbackKey`.
- `IDatabaseService` and `IMongoDbDatabaseService` → one class deriving from `MongoDbDatabaseService`, registered once as a singleton and exposed under both keyed interfaces (plus any unkeyed bot-specific interface). Its database defaults to the lowercase bot name.

Services receive their identity with `[FromKeyedServices(Constants.Name)] BotIdentity identity`. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) for what the base classes do and [Persistence and Backups](../architecture/persistence-and-backups.md) for the database base class.

## Update controller

Add a controller deriving from `UpdateControllerBase`, with `[ApiController]` and `[Route("api/<Name>/[controller]")]`, named `UpdateController`. It injects the keyed `IUpdateService` together with `IWebhookSecretProvider`, `IUpdateDeliveryGuard`, `INotificationService`, and a logger. The route must match the URL that the webhook initializer registers, `<base>/api/<Name>/update` (routing is case-insensitive). The base class handles secret checks, the readiness gate, duplicates, and retry semantics; see [Telegram Webhook Integration](../integrations/telegram-webhooks.md).

## Health checks, pages, and text

- In `AddHealthChecks`, register checks on the shared builder. TestBot calls `AddBotStatsHealthCheck<ITestBotDatabaseService>(name)`, which reports the chat count and is unhealthy when the query fails.
- Routable pages in the module's components assembly are served automatically. The layout links each module to `/<Name>/dashboard`, so provide a page there. TestBot's page composes the shared backup, chat, and stats components.
- Localized text is loaded from `Languages/<botName>/<locale>.json` in the app's output folder. `GetLocalizedString` returns an empty string for a missing key.

## Tests

Mirror `TestBot.UnitTests`: a module test that discovers the module from its assembly, checks `AddServices` registrations under the bot name, and verifies the health check. Add controller tests for the update endpoint and focused tests for handlers and the update service. `WebApplicationExtensionsTests` check that the host registers every referenced module. See [Testing Strategy](../testing/strategy.md).
