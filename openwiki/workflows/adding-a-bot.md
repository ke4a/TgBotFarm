---
type: workflow
title: Adding a Bot Module
description: Step-by-step guide to adding a Telegram bot to the BotFarm host, from scaffolding with scripts/new-bot.sh or new-bot.ps1 and the botfarm-bot template to the module contract, covering discovery, naming, validated configuration, the keyed services the host expects, the update controller, health checks, pages, and tests, with TestBot as the reference.
tags: [workflow, bots, modules, extension]
sources:
  - id: openwiki-source-4236d4bb95f416e69dc8cc45
    resource: repo://BotFarm.Core/Abstractions/IBotModule.cs
  - id: openwiki-source-e001cba704267f13f505ac68
    resource: repo://BotFarm.Core/Abstractions/IBotRegistry.cs
  - id: openwiki-source-d150e8d260bda8b7c7d64ae2
    resource: repo://BotFarm.Core/Base/BotModuleAttribute.cs
  - id: openwiki-source-c20f310ef0f21eb05cbaa2d6
    resource: repo://BotFarm.Core/Base/MongoDbDatabaseService.cs
  - id: openwiki-source-60145f991035e51f8a6872fc
    resource: repo://BotFarm.Core/Base/UpdateService.cs
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
  - id: openwiki-source-23775c3de52f3ab95a13cb8b
    resource: repo://README.md
  - id: openwiki-source-7cf80f43b7adbaefc9a638c2
    resource: repo://scripts/new-bot.ps1
  - id: openwiki-source-b44ec18a3b072cd326f72a45
    resource: repo://scripts/new-bot.sh
  - id: openwiki-source-6f14174db8a735fbc4fe0e7d
    resource: repo://templates/BotFarm.Bot/.template.config/template.json
  - id: openwiki-source-edd7baf97b47abd1e0301d31
    resource: repo://templates/BotFarm.Bot/BotTemplate/AdminLanguages/BotTemplate/en-US.json
  - id: openwiki-source-a1e0fd57e9011c8f461629a7
    resource: repo://templates/BotFarm.Bot/BotTemplate/BotTemplateModule.cs
  - id: openwiki-source-432b071f5fd78f9fb202395a
    resource: repo://templates/BotFarm.Bot/BotTemplate/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-fed6ea130f0d83ea0ffac32d
    resource: repo://templates/BotFarm.Bot/BotTemplate/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-ceee3ef0e7a6273e996e6b7c
    resource: repo://templates/BotFarm.Bot/BotTemplate/Pages/BotTemplateDashboard.razor
  - id: openwiki-source-e7d412fca391eeb140b4b1e7
    resource: repo://templates/BotFarm.Bot/BotTemplate/Services/BotTemplateDatabaseService.cs
  - id: openwiki-source-254c9b517d5aa3409beaff97
    resource: repo://TestBot/Controllers/UpdateController.cs
  - id: openwiki-source-b273e7de5ce8bb6689e05057
    resource: repo://TestBot/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-7f1ffe359debc4d1ba6e1788
    resource: repo://TestBot/Models/GifData.cs
  - id: openwiki-source-66cfed0bff7015b74aadf547
    resource: repo://TestBot/Pages/TestBotDashboard.razor
  - id: openwiki-source-4cf4590d989180df848e74b8
    resource: repo://TestBot/Services/TestBotDatabaseService.cs
  - id: openwiki-source-f9f5e32d1481d29435f8e1e0
    resource: repo://TestBot/TestBotModule.cs
generated: { by: "claude-code", at: "2026-11-11T20:41:09.729Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-11T20:41:09.729Z
---

# Adding a Bot Module

A bot is a separate Razor class library that the host finds when it starts. The library contributes one `IBotModule`; the host never names bots in code. The fastest start is the scaffolding script, which generates a working skeleton from a `dotnet new` template; `TestBot` remains the reference for complete commands, callbacks, and bot data.

## Scaffolding a bot

Run `scripts/new-bot.sh <BotName> [emoji]` (Bash; needs `dotnet` and `jq`) or `scripts/new-bot.ps1 <BotName> [emoji]` (PowerShell 7+; needs `dotnet`). `<BotName>` must be PascalCase letters and digits; the emoji defaults to 🤖. Before changing anything, the script refuses to run if `<BotName>/` or `tests/<BotName>.UnitTests/` already exists or any `BotFarm/appsettings*.json` already has a `Bots:<BotName>` section. It then:

1. installs `templates/BotFarm.Bot` with `dotnet new install --force` and runs `dotnet new botfarm-bot --name <BotName>` at the repository root;
2. adds the bot project and its unit test project (in the `tests` solution folder) to `BotFarm.sln`;
3. adds a project reference from `BotFarm/BotFarm.csproj`;
4. writes a disabled `Bots:<BotName>:BotConfig` section (`Enabled: false`, the emoji, placeholder `Token`, `AdminChatId: 0`, empty `WebhookSecret`) to every `BotFarm/appsettings*.json`;
5. builds the solution and prints the next steps.

The template's source name `BotTemplate` becomes the bot name, and `bottemplate` becomes its lowercase form in file names, the logo path, and the dashboard route. The generated project contains:

- `<BotName>Module` declared with `[assembly: BotModule]`, adding services and the stats health check;
- `Constants` with the bot name and empty `Commands` and `Callbacks` structs to fill in;
- `ServiceCollectionExtensions` registering the config binding, markup service, keyed update service, keyed bot service, and one database service exposed as keyed `IDatabaseService` and `IMongoDbDatabaseService` plus the bot's own interface;
- an update service deriving from `UpdateService<<BotName>ChatSettings>` with an empty `OnUpdate`, an update controller, a bot service, and a database service deriving from `MongoDbDatabaseService`;
- a dashboard page at `/<botname>/dashboard` composing the shared components and reading its own texts from an admin translation set, a placeholder logo under `wwwroot/img/`, `en-US`, `ru-RU`, and `uk-UA` chat-message translations under `Languages/<BotName>/`, and matching admin UI translations under `AdminLanguages/<BotName>/`;
- `tests/<BotName>.UnitTests` with module tests for discovery, keyed registrations, and the health check.

After scaffolding, set the bot's `Token` and `AdminChatId` (preferably through `Bots__<BotName>__BotConfig__*` or user secrets), set `Enabled` to `true`, replace the logo, and add command and callback handlers registered as keyed services. The template folder is not in the solution and is excluded from the Docker build context, so its placeholder projects are never built with the host.

The sections below describe the contract the generated code follows, and are what to check when setting up a bot by hand.

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
- `IUpdateService` → a class deriving from `UpdateService<TChatSettings>`, keyed scoped. The base class already routes `/start`, `/changelanguage`, language callbacks, the bot-joined welcome, and supergroup chat migration. Override `OnUpdate` for the bot's own behavior, `OnChatMigrated` for one-time side effects after a chat's data has moved to its new supergroup ID (the default does nothing; send any message to the new ID, because the old group no longer accepts messages), or `ProcessUpdate` for entirely different routing.
- `ICommandHandler` / `ICallbackHandler` implementations, keyed scoped. They are injected into the update service with `[FromKeyedServices(name)]` and dispatched by their `Command` or `CallbackKey`.
- `IDatabaseService` and `IMongoDbDatabaseService` → one class deriving from `MongoDbDatabaseService`, registered once as a singleton and exposed under both keyed interfaces (plus any unkeyed bot-specific interface). Its database defaults to the lowercase bot name.

Services receive their identity with `[FromKeyedServices(Constants.Name)] BotIdentity identity`. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) for what the base classes do and [Persistence and Backups](../architecture/persistence-and-backups.md) for the database base class.

## Storing bot data

Keep each kind of bot data in one collection, with the chat (and user) it belongs to as the document `_id`, as TestBot does with `GifData`. A collection per chat is not recommended. Put uniqueness in the compound `_id` rather than a unique index: backups store documents only and a restore recreates collections without secondary indexes, but `_id` is always unique. To read or delete everything for one chat, query an `_id` range with the chat ID fixed and the second key part from `MinKey` to `MaxKey`; this works only if the key class serializes the chat ID first (TestBot uses `[BsonElement(Order = …)]`).

When a group becomes a supergroup, the base update service calls `MigrateChat` once, from the upgrade notice Telegram posts in the new supergroup, and Core moves the chat's `ChatSettings` to the new chat ID itself. If the bot stores other per-chat data, override `MigrateBotChatData(fromChatId, toChatId)` in the `MongoDbDatabaseService` subclass. The override must touch only the bot's own database, keep data already stored under the new chat ID when both chats have a record, and be safe to repeat: completing it after a partial run must give the same end state. TestBot's override is the reference. See [Persistence and Backups](../architecture/persistence-and-backups.md#chat-migration).

The dashboard can delete a chat, or every chat the bot can no longer reach, through `IDatabaseService.DeleteChat`, which removes the chat's settings. If the bot stores other per-chat data, also override `DeleteBotChatData(chatId)`; it runs before the settings are deleted. It must touch only the bot's own database and be safe to repeat, including for a chat with no data. TestBot's override calls `ClearChatData`. See [Persistence and Backups](../architecture/persistence-and-backups.md#chat-deletion).

## Update controller

Add a controller deriving from `UpdateControllerBase`, with `[ApiController]` and `[Route("api/<Name>/[controller]")]`, named `UpdateController`. It injects the keyed `IUpdateService` together with `IWebhookSecretProvider`, `IUpdateDeliveryGuard`, `INotificationService`, and a logger. The route must match the URL that the webhook initializer registers, `<base>/api/<Name>/update` (routing is case-insensitive). The base class handles secret checks, the readiness gate, duplicates, and retry semantics; see [Telegram Webhook Integration](../integrations/telegram-webhooks.md).

## Health checks, pages, and text

- In `AddHealthChecks`, register checks on the shared builder. TestBot calls `AddBotStatsHealthCheck<ITestBotDatabaseService>(name)`, which reports the chat count and is unhealthy when the query fails.
- Routable pages in the module's components assembly are served automatically. The layout links each module to `/<Name>/dashboard` (with the configured emoji, or a robot icon when none is set), so provide a page there. The page fills the layout's `TitleSection` and `LogoSection` with `SectionContent` and sets its own `PageTitle`. TestBot's page does this, also fills the `FooterSection`, and composes the shared backup and chat cards in the wide column and the webhook (`DashboardWebhook`) and stats cards in the narrow one.
- Text the bot sends to chats is loaded from `Languages/<botName>/<locale>.json` in the app's output folder. `GetLocalizedString` returns an empty string for a missing key.
- Text on the bot's dashboard page comes from the admin UI translation set `AdminLanguages/<BotName>/<culture>.json`, named after the bot's assembly. Inject `IStringLocalizer<YourPage>` and use keys such as `Page.Title`, `Page.Heading`, `Page.LogoAlt` and `Page.Footer`, as the template and TestBot do. A set must have an `en-US` file and may use only the languages of the shared set; a key missing in the current language falls back to English. The shared cards are already localized. See [Admin UI Localization](../architecture/admin-localization.md).

## Tests

The scaffolded test project already has the module tests. Otherwise mirror `TestBot.UnitTests`: a module test that discovers the module from its assembly, checks `AddServices` registrations under the bot name, and verifies the health check. Add controller tests for the update endpoint and focused tests for handlers and the update service. `WebApplicationExtensionsTests` check that the host registers every referenced module. See [Testing Strategy](../testing/strategy.md).
