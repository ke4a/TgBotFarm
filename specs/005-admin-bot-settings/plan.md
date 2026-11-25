# Implementation Plan: Admin-Managed Bot Settings

**Branch**: `[master]` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/005-admin-bot-settings/spec.md`

## Summary

Each bot's token, admin chat, webhook secret and emoji move into a `BotSettings` collection in the
`BotFarmControl` MongoDB database. Tokens and webhook secrets are stored encrypted with the host's
existing data-protection key ring. Administrators edit them from a settings card on each bot's
dashboard page.

Readers keep using `IOptionsMonitor<BotConfig>`. A settings provider registered in DI fills the named
options from its decrypted in-memory snapshot and signals a change after every save, so new values
apply without a restart. `BotControlCoordinator` loads settings before enabled state in its existing
per-bot pass. Bots without usable settings get a new `NotConfigured` state, stay gated and cannot be
enabled, and the application always starts. Bot services create their Telegram client when settings
become usable and recreate it when the token changes.

Bot settings are never read from configuration: after an upgrade, an administrator enters each bot's
token and admin chat on the dashboard. `Enabled` leaves configuration entirely, and new bots start
disabled. Research decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- Existing: ASP.NET Core and Blazor Server, ASP.NET Core Data Protection (shared framework),
  MudBlazor 9.14, Telegram.Bot 22.10, MongoDB.Driver.
- No new packages.

**Storage**: MongoDB. A new `BotSettings` collection in the existing `BotFarmControl` database, with a
sparse unique index on `telegramBotId`. The data-protection key ring stays in `<data>/dpkeys`.

**Testing**:
- NUnit 5 with NSubstitute for unit tests; `EphemeralDataProtectionProvider` where real protection
  is needed.
- bUnit 2.15 for the settings card, edit dialog and enabled control.
- Testcontainers MongoDB in `BotFarm.IntegrationTests` for the settings store and host startup.
- No test calls Telegram; the existing `FakeTelegramApi` serves host tests.

**Target Platform**: Linux container (non-root) and local development on macOS or Windows.

**Project Type**: Web service with a Blazor admin UI, plus bot plugin projects.

**Performance Goals**:
- Reading settings stays an in-memory options lookup; the webhook path does no database or
  decryption work.
- A save makes a bounded number of calls: one Telegram verification, one duplicate-account lookup
  and one MongoDB write, plus deleting the old webhook after an account change and the existing
  re-apply when needed.

**Constraints**:
- Secrets never leave memory in plain form: not in MongoDB, logs, telemetry, health output, alerts,
  backups or rendered pages (FR-003, FR-004, SC-002).
- Startup never fails because of one bot's settings (FR-010, SC-005).
- No configuration fallback when the store cannot be read (FR-015).
- Single application instance.

**Scale/Scope**:
- A handful of bots, one document each.
- 1 new collection, 1 settings service and store, 1 dashboard card and dialog, translations in
  3 languages.
- Removal of `Enabled` and of startup settings validation; updates to the template, new-bot scripts,
  `appsettings*.json` and the README.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| I. Layered ownership | Store, service, validation, options wiring and coordinator changes live in BotFarm.Core. The settings card and dialog live in BotFarm.Shared. The host keeps registering data protection. Bots change only the `AddBotConfig` call. No new project references. | Pass |
| II. Secure configuration and access | Settings reach readers as named options filled from a store-backed service, which principle II allows; the stored token and webhook secret are encrypted at rest. Secrets are removed from tracked files; the dashboard only accepts them and never shows them. Editing uses the existing authenticated dashboard access. Logs and dashboard errors carry setting names, categories and Telegram error codes, never values or Telegram exception messages. | Pass |
| III. Behavior-focused tests | Unit tests with substitutes cover each requirement's behavior. The MongoDB store gets Testcontainers integration coverage like the control-state store, since it is a new persistence boundary. Those tests use a disposable local MongoDB, not a deployed service, and stay in `BotFarm.IntegrationTests`, which CI runs as its own step apart from the routine suite. No test uses live Telegram. | Pass |
| IV. Isolate and protect persistent data | Settings live in the shared `BotFarmControl` database, which principle IV names as the exception for platform control data such as enabled state and settings. Per-bot databases and their backups and restores are untouched and never contain settings. | Pass |
| V. Operable and recoverable services | The application always starts. Not configured, unreadable and unknown states are visible on the dashboard and in logs. Retry reloads settings. Key loss is recovered by re-entering the token, and store loss by re-entering settings; both are documented. | Pass |
| Development workflow: persisted data and deployment | Upgrading needs each bot's token and admin chat entered once on the dashboard; saved on/off states are kept. Roll forward is supported; rollback needs the settings kept in configuration, which the README states. | Pass |
| Additional constraints: runtime dependencies | `emoji-picker-element` (Apache-2.0, static files) is added for the emoji picker, because typed emoji are hard to validate. It is vendored with its English data, loaded only when the picker opens, and makes no network requests. | Pass |

**Post-design re-check (after Phase 1)**: Still passing. The design changes these shared contracts,
all within BotFarm.Core and with every caller in this repository:
- `AddBotConfig(string botName)` loses its configuration parameter (TestBot, the template and the
  new-bot scripts are updated);
- `BotConfig` loses `Enabled`;
- `BotRuntimeState` gains `NotConfigured`;
- `BotService` creates its client lazily, and `Client` throws `BotNotConfiguredException` while the
  bot has no usable token;
- `IBotSettingsService` is new.

## Compatibility and Rollback

- **Bots and template**: Update the `AddBotConfig` call. Code that reads `Client` while the bot is
  not configured gets `BotNotConfiguredException`; bot handlers only run for enabled bots, which
  always have a client.
- **Configuration**: `Bots:*:BotConfig` values, including `Enabled`, are no longer read. After the
  upgrade each bot is "Not configured" until its token and admin chat are entered on the dashboard.
- **Data**: The `BotSettings` collection is created on the first save. Existing
  `BotControlStates` documents keep each bot's enabled state.
- **Rollback**: The previous version reads settings only from configuration, so keep the values in
  configuration until the upgrade is confirmed (or put them back) before rolling back. The `BotSettings` collection can be left in
  place; the previous version ignores it.

## Project Structure

### Documentation (this feature)

```text
specs/005-admin-bot-settings/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Stored document, states, commands and change rules
├── quickstart.md        # Validation guide
├── contracts/
│   └── bot-settings.md  # Settings service, dashboard, endpoint, logs and configuration contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
BotFarm.Core/
├── Abstractions/
│   ├── IBotService.cs                # ReloadClient
│   ├── IBotSettingsProvider.cs       # new: load, snapshot, view, persist
│   ├── IBotSettingsService.cs        # new: view and save
│   ├── IBotSettingsStore.cs          # new: document reads, insert-if-absent, writes
│   └── ITelegramTokenVerifier.cs     # new
├── Base/
│   └── BotService.cs                 # lazy client creation and recreation
├── Exceptions/
│   └── BotNotConfiguredException.cs  # new
├── Extensions/
│   └── ServiceCollectionExtensions.cs # AddBotConfig(botName), settings registrations
├── Models/
│   ├── BotConfig.cs                  # Enabled removed
│   ├── BotRuntimeStatus.cs           # NotConfigured state
│   └── BotSettings*.cs               # new: document, view, change, save result, state
└── Services/
    ├── BotConfigValidator.cs         # value rules only; no startup validation
    ├── BotControlCoordinator.cs      # settings load, NotConfigured, seed disabled; implements IBotSettingsService
    ├── BotSettingsOptions.cs         # new: named options setup and change token source
    ├── BotSettingsProtector.cs       # new: data-protection wrapper
    ├── BotSettingsProvider.cs        # new: snapshot, decryption, persistence
    ├── MongoBotSettingsStore.cs      # new
    ├── TelegramBroadcastService.cs   # fail before sending without usable settings
    ├── TelegramTokenVerifier.cs      # new: GetMe through a temporary client
    └── TelegramNotificationService.cs # skip alerts, fail sends without usable settings

BotFarm.Shared/
├── libman.json                       # vendored front-end libraries: quill, emoji-picker-element and its English data
├── wwwroot/lib/                      # quill, emoji-picker-element, emoji-picker-element-data
├── AdminLanguages/BotFarm.Shared/    # en-US, ru-RU, uk-UA strings
└── Components/
    ├── BotDashboard.razor            # adds the settings card
    ├── BotEnabledControl.razor(.cs)  # NotConfigured display
    ├── DashboardBotSettings.razor(.cs) # new settings card
    ├── BotSettingsDialog.razor(.cs,.js) # new edit dialog with the emoji picker
    ├── DashboardChats.razor.cs       # not configured message; Telegram errors without exception text
    ├── DashboardWebhook.razor(.cs)   # not configured message
    └── SendMessageDialog.razor.cs    # not configured message; Telegram errors without exception text

BotFarm/
├── appsettings.json, appsettings.Development.json  # Enabled removed; bot values optional
└── Extensions/WebApplicationExtensions.cs          # unchanged data-protection setup reused

TestBot/Extensions/ServiceCollectionExtensions.cs   # AddBotConfig call
templates/BotFarm.Bot/BotTemplate/Extensions/ServiceCollectionExtensions.cs
scripts/new-bot.sh, scripts/new-bot.ps1             # no Enabled; settings set from the dashboard
README.md                                           # configuration, upgrade, recovery, rollback

tests/
├── BotFarm.Core.UnitTests/
│   ├── Extensions/BotConfigRegistrationTests.cs
│   └── Services/                     # validator, protector, provider, audit
│                                     # logs, options, coordinator, BotService, notifications, broadcast
├── BotFarm.Shared.UnitTests/Components/  # settings card, dialog, enabled control, chats, webhook,
│                                         # send message dialog
├── BotFarm.UnitTests/                # existing host tests, run unchanged
└── BotFarm.IntegrationTests/
    ├── Persistence/                  # settings store, encryption at rest
    ├── Backups/MongoDbBackupServiceTests.cs  # backup and restore leave settings alone
    └── Host/                         # startup with missing and configuration-only settings, secret
                                      # exposure in health output and exported telemetry
```

**Structure Decision**: Follow the existing layering. Settings logic sits in BotFarm.Core next to the
bot control coordinator and its control-state store, UI in BotFarm.Shared next to the other dashboard
cards, and host composition stays in BotFarm. No new projects.

## Complexity Tracking

No constitution violations to justify.
