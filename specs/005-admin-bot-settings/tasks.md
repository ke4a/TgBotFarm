---

description: "Task list for Admin-Managed Bot Settings"
---

# Tasks: Admin-Managed Bot Settings

**Input**: Design documents from `/specs/005-admin-bot-settings/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/bot-settings.md](./contracts/bot-settings.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (principle III) requires focused behavior tests for every change. Write each phase's tests first and make sure they fail before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US4)

## Path Conventions

The repository's own layout: `BotFarm.Core/`, `BotFarm.Shared/`, `BotFarm/` (host), `TestBot/`, `templates/BotFarm.Bot/BotTemplate/`, `scripts/`, `tests/<project>/`.

## Component Map

- `MongoBotSettingsStore` (`IBotSettingsStore`): MongoDB reads and writes of `BotSettings` documents.
- `BotSettingsProtector`: protects and unprotects secrets with purposes `BotFarm.BotSettings.v1` and the bot name.
- `BotSettingsProvider` (`IBotSettingsProvider`): loads and decrypts settings, holds the in-memory snapshot, builds the view, persists accepted changes and signals options changes. It does not depend on the coordinator.
- `BotSettingsOptionsSetup` and `BotSettingsChangeTokenSource`: fill named `BotConfig` options from the snapshot and invalidate them after a change.
- `TelegramTokenVerifier` (`ITelegramTokenVerifier`): checks a token with `GetMe` through a temporary client.
- `BotControlCoordinator`: also implements `IBotSettingsService` (registered as a forwarding singleton like `IBotControlService`), so saves run under the bot's operation lock and can re-apply the bot.

---

## Phase 1: Setup (Shared Types)

**Purpose**: Declare the models and abstractions every later phase uses.

- [X] T001 [P] Add the stored `BotSettings` document (fields and nullability from data-model.md, mapped like `BotControlState`) in `BotFarm.Core/Models/BotSettings.cs`.
- [X] T002 [P] Add `BotSettingsState` (`Unknown`, `Incomplete`, `Unreadable`, `Configured`) and `BotSettingsView` (fields from data-model.md, no secret fields) in `BotFarm.Core/Models/BotSettingsView.cs`.
- [X] T003 [P] Add `BotSettingsChange` (fields and rules from data-model.md) in `BotFarm.Core/Models/BotSettingsChange.cs`.
- [X] T004 [P] Add `BotSettingsSaveResult` (`Saved` with view, `BotRuntimeStatus` and the verified Telegram username when a token was verified, `NeedsAccountChangeConfirmation` with stored and new bot IDs, `Rejected` with a list of `BotSettingsRejection`) and the `BotSettingsRejection` categories from contracts/bot-settings.md in `BotFarm.Core/Models/BotSettingsSaveResult.cs`.
- [X] T005 [P] Add `BotNotConfiguredException` (bot name only in the message) in `BotFarm.Core/Exceptions/BotNotConfiguredException.cs`.
- [X] T006 [P] Add `IBotSettingsStore` (`GetAsync`, `UpsertAsync`, `FindBotNameByTelegramBotIdAsync`) in `BotFarm.Core/Abstractions/IBotSettingsStore.cs`.
- [X] T007 [P] Add `IBotSettingsProvider` (`LoadAsync(botName)` returning `BotSettingsState`, `GetState`, `GetView`, `PersistAsync`) in `BotFarm.Core/Abstractions/IBotSettingsProvider.cs`.
- [X] T008 [P] Add `IBotSettingsService` (`GetView`, `SaveAsync`) with contract-only doc comments in `BotFarm.Core/Abstractions/IBotSettingsService.cs`.
- [X] T009 [P] Add `ITelegramTokenVerifier` returning the verified bot ID and username, or `RejectedByTelegram` / `TelegramUnreachable`, in `BotFarm.Core/Abstractions/ITelegramTokenVerifier.cs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Stored, encrypted settings feed `IOptionsMonitor<BotConfig>`; bots without usable settings no longer stop startup and every Telegram operation on them fails cleanly; `Enabled` leaves configuration and new bots start disabled.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests for Phase 2

- [X] T010 [P] Rewrite validator tests for the value rules (token format and bot ID extraction, non-zero admin chat, webhook secret charset and length) without options validation in `tests/BotFarm.Core.UnitTests/Services/BotConfigValidatorTests.cs`.
- [X] T011 [P] Add protector tests: round trip, a value protected for one bot fails for another, a different key ring throws `CryptographicException`, in `tests/BotFarm.Core.UnitTests/Services/BotSettingsProtectorTests.cs`.
- [X] T012 [P] Add store persistence tests: insert-if-absent never overwrites, upsert replaces, the sparse unique `telegramBotId` index rejects a second bot with the same ID and allows several nulls, lookup by bot ID, in `tests/BotFarm.IntegrationTests/Persistence/MongoBotSettingsStoreTests.cs`.
- [X] T013 [P] Add provider tests: no document gives `Incomplete`; a complete document gives `Configured` and `IOptionsMonitor<BotConfig>.Get(bot)` returns the decrypted values; a store failure gives `Unknown` and leaves options empty without reading configuration; `PersistAsync` updates the snapshot and fires the change token, in `tests/BotFarm.Core.UnitTests/Services/BotSettingsProviderTests.cs`.
- [X] T014 [P] Update `BotService` tests: the constructor no longer needs a token; reading `Client` without a usable token throws `BotNotConfiguredException`; the client is created on first access; `ReloadClient` makes the next access use the new token, in `tests/BotFarm.Core.UnitTests/Services/BotServiceTests.cs`.
- [X] T015 [P] Update coordinator initialization tests: settings load before control state for each bot; a `NotConfigured` bot still has its control state loaded (or seeded disabled when missing), reports `BotRuntimeState.NotConfigured` with that desired state and applied false, stays gated and rejects `SetEnabledAsync`; a missing control state is seeded disabled whatever configuration says; a settings store failure leaves the bot `Unknown`; `RetryAsync` reloads settings; `GetWebhookInfoAsync` and `DropPendingUpdatesAsync` throw `BotNotConfiguredException` for a `NotConfigured` bot without calling Telegram, in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorInitializationTests.cs`.
- [X] T016 [P] Update registration tests: `AddBotConfig(botName)` registers named options filled from the provider, does not bind `Bots:{bot}:BotConfig`, and the host starts with no bot settings, in `tests/BotFarm.Core.UnitTests/Extensions/BotConfigRegistrationTests.cs`.
- [X] T017 [P] Add notification tests: an admin alert for a bot without a usable token or admin chat is logged as a warning and not sent; `SendMessage` to a chat for a bot without a usable token throws `BotNotConfiguredException`, in `tests/BotFarm.Core.UnitTests/Services/TelegramNotificationServiceTests.cs`.
- [X] T018 [P] Add broadcast tests: `Broadcast` for a bot without a usable token throws `BotNotConfiguredException` before sending anything and leaves no running broadcast for the bot, in `tests/BotFarm.Core.UnitTests/Services/TelegramBroadcastServiceTests.cs`.

### Implementation for Phase 2

- [X] T019 Turn `BotConfigValidator` into static value rules (token, bot ID from token, admin chat, webhook secret) with the existing messages, and drop its `IValidateOptions<BotConfig>` implementation, in `BotFarm.Core/Services/BotConfigValidator.cs`.
- [X] T020 [P] Implement `BotSettingsProtector` over `IDataProtectionProvider` in `BotFarm.Core/Services/BotSettingsProtector.cs`.
- [X] T021 [P] Implement `MongoBotSettingsStore` on the `BotSettings` collection of `BotFarmControl`, creating the sparse unique `telegramBotId` index on first use, following `MongoBotControlStateStore`, in `BotFarm.Core/Services/MongoBotSettingsStore.cs`.
- [X] T022 Implement `BotSettingsProvider`: per-bot load into a decrypted snapshot, state per bot, view building, `PersistAsync` (protect, upsert, update snapshot, signal), in `BotFarm.Core/Services/BotSettingsProvider.cs`.
- [X] T023 Implement `BotSettingsOptionsSetup` (`IConfigureNamedOptions<BotConfig>` copying the snapshot) and `BotSettingsChangeTokenSource` (`IOptionsChangeTokenSource<BotConfig>` per bot name, fired by the provider) in `BotFarm.Core/Services/BotSettingsOptions.cs`.
- [X] T024 Remove `Enabled` from `BotConfig` and update its doc comments in `BotFarm.Core/Models/BotConfig.cs`.
- [X] T025 Change `AddBotConfig` to `AddBotConfig(string botName)` registering the bot's options and change token source; register the store, protector, provider and options setup in `AddCoreServices`; remove the validator registration and `ValidateOnStart`, in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`.
- [X] T026 [P] Update the `AddBotConfig` call in `TestBot/Extensions/ServiceCollectionExtensions.cs`.
- [X] T027 [P] Update the `AddBotConfig` call in `templates/BotFarm.Bot/BotTemplate/Extensions/ServiceCollectionExtensions.cs`.
- [X] T028 Add `ReloadClient()` to `IBotService` in `BotFarm.Core/Abstractions/IBotService.cs`, and make `BotService` create its client lazily from the current options token, throw `BotNotConfiguredException` without one, and drop the cached client on `ReloadClient`, thread-safely, in `BotFarm.Core/Base/BotService.cs`.
- [X] T029 Add `NotConfigured` to `BotRuntimeState` in `BotFarm.Core/Models/BotRuntimeStatus.cs`.
- [X] T030 In `BotControlCoordinator`, load settings through `IBotSettingsProvider` before control state under the bot's lock; load (or seed disabled) control state for every bot whose settings could be read; report `NotConfigured` (saved desired state, applied false) or `Unknown`; reject `SetEnabledAsync` for `NotConfigured`; make `RetryAsync` accept `NotConfigured` and reload settings; make `GetWebhookInfoAsync` and `DropPendingUpdatesAsync` throw `BotNotConfiguredException` for `NotConfigured` bots, in `BotFarm.Core/Services/BotControlCoordinator.cs`.
- [X] T031 Skip and log (warning, bot name only) admin alerts for a bot without a usable token or admin chat, and let `SendMessage` to a chat throw `BotNotConfiguredException` for a bot without a usable token, in `BotFarm.Core/Services/TelegramNotificationService.cs`.
- [X] T032 [P] Resolve the bot's client before registering a running broadcast so `Broadcast` fails with `BotNotConfiguredException` before sending, in `BotFarm.Core/Services/TelegramBroadcastService.cs`.

**Checkpoint**: The application starts with no bot settings, every bot shows `NotConfigured` and gets HTTP 503, stored settings reach `IOptionsMonitor<BotConfig>`, Telegram operations on unconfigured bots throw `BotNotConfiguredException`, and all existing tests pass.

---

## Phase 3: User Story 1 - Configure a bot from the dashboard (Priority: P1) 🎯 MVP

**Goal**: Administrators set and change token, admin chat, webhook secret and emoji from the dashboard, and changes apply without a restart.

**Independent Test**: With TestBot `NotConfigured`, enter a valid token and admin chat in the dashboard, enable the bot, and verify it receives updates and sends admin alerts to that chat without a restart.

### Tests for User Story 1

- [X] T033 [P] [US1] Add token verifier tests: success returns bot ID and username; `ApiRequestException` 401/404 gives `RejectedByTelegram`; `HttpRequestException`, timeouts and other API errors give `TelegramUnreachable`, in `tests/BotFarm.Core.UnitTests/Services/TelegramTokenVerifierTests.cs`.
- [X] T034 [P] [US1] Add save tests: first save on a `NotConfigured` bot stores settings, returns `Configured` and leaves the bot disabled; format errors return one rejection per setting; `RejectedByTelegram`, `TelegramUnreachable` and `DuplicateAccount` keep stored settings; a different bot ID returns `NeedsAccountChangeConfirmation` and saves only with `ConfirmAccountChange`; admin chat and emoji changes do not re-apply; a token change on an enabled bot deletes the old webhook (failure ignored), reloads the client and re-applies; a token change on a disabled bot only reloads the client; setting or clearing the webhook secret re-applies an enabled bot; `UseDerivedWebhookSecret` with `WebhookSecret` is rejected; re-apply changes are `BotBusy` while `Unknown` or `Pending`; a token or webhook secret change on a bot in `Error` is stored and re-applies its desired on/off state with the new client; a failed write returns `StoreUnavailable`; a save while another operation holds the bot returns `BotBusy`; a save that completes a `NotConfigured` bot applies its saved desired state, in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorSettingsTests.cs`.
- [X] T035 [P] [US1] Add settings card tests: renders state, bot ID, the username when the view has one, admin chat, emoji, custom secret flag and last change; opens the dialog, in `tests/BotFarm.Shared.UnitTests/Components/DashboardBotSettingsTests.cs`.
- [X] T036 [P] [US1] Add dialog tests: secret inputs start empty and an empty input keeps the stored value; the derived-secret action; account change confirmation shows both bot IDs and resubmits with confirmation; a successful save that verified a token names the Telegram username; each rejection shows its localized message; the current user's name is sent as `ChangedBy`, in `tests/BotFarm.Shared.UnitTests/Components/BotSettingsDialogTests.cs`.
- [X] T037 [P] [US1] Add enabled control tests: `NotConfigured` shows "Not configured", disables the switch and has an "Open settings" button that opens the settings dialog, reloads the status after a save and reports it, and does nothing after a cancel, in `tests/BotFarm.Shared.UnitTests/Components/BotEnabledControlTests.cs`.
- [X] T038 [P] [US1] Add tests that a `BotNotConfiguredException` shows a "not configured" message instead of an error in the chats card (chat loading and broadcast), the webhook card (webhook info and dropping pending updates) and the send message dialog, in `tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs`, `tests/BotFarm.Shared.UnitTests/Components/DashboardWebhookRenderTests.cs` and `tests/BotFarm.Shared.UnitTests/Components/SendMessageDialogTests.cs`.
- [X] T039 [P] [US1] Add a host test: start with no TestBot settings, confirm startup and HTTP 503, save settings through `IBotSettingsService` against `FakeTelegramApi`, enable TestBot and confirm an update is processed, in `tests/BotFarm.IntegrationTests/Host/BotSettingsStartupTests.cs`.

### Implementation for User Story 1

- [X] T040 [P] [US1] Implement `TelegramTokenVerifier` with a temporary client from `ITelegramBotClientFactory`, mapping exceptions to categories without reading their messages, in `BotFarm.Core/Services/TelegramTokenVerifier.cs`, and register it in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`.
- [X] T041 [US1] Implement `IBotSettingsService` on `BotControlCoordinator` (`GetView` from the provider; `SaveAsync` with validation, verification, duplicate and account checks, persist, client reload and re-apply as in data-model.md's change flow, returning the verified username), with the view's username taken from the bot's `Me` when its ID matches the stored bot ID, in `BotFarm.Core/Services/BotControlCoordinator.cs`, and register the forwarding singleton in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`.
- [X] T042 [P] [US1] Create the settings card in `BotFarm.Shared/Components/DashboardBotSettings.razor` and `BotFarm.Shared/Components/DashboardBotSettings.razor.cs`.
- [X] T043 [P] [US1] Create the edit dialog with plain text secret inputs (`autocomplete="off"`, no spellcheck or capitalization, password-manager ignore attributes, cleared on open and after save), the derived-secret action and the account change confirmation in `BotFarm.Shared/Components/BotSettingsDialog.razor` and `BotFarm.Shared/Components/BotSettingsDialog.razor.cs`.
- [X] T044 [US1] Add the settings card to the bot page in `BotFarm.Shared/Components/BotDashboard.razor`.
- [X] T045 [P] [US1] Show the "Not configured" state, disable the switch and add an "Open settings" button that opens the settings dialog in `BotFarm.Shared/Components/BotEnabledControl.razor` and `BotFarm.Shared/Components/BotEnabledControl.razor.cs`.
- [X] T046 [P] [US1] Handle `BotNotConfiguredException` with a "not configured" message in `BotFarm.Shared/Components/DashboardChats.razor.cs` (chat loading and broadcast), `BotFarm.Shared/Components/DashboardWebhook.razor` and `BotFarm.Shared/Components/DashboardWebhook.razor.cs` (webhook info and dropping pending updates), and `BotFarm.Shared/Components/SendMessageDialog.razor.cs`.
- [X] T047 [US1] Add all new card, dialog, state, "not configured" and rejection strings to `BotFarm.Shared/AdminLanguages/BotFarm.Shared/en-US.json`, `BotFarm.Shared/AdminLanguages/BotFarm.Shared/ru-RU.json` and `BotFarm.Shared/AdminLanguages/BotFarm.Shared/uk-UA.json`.

**Checkpoint**: User Story 1 works end to end on a fresh installation (quickstart section 2).

---

## Phase 4: User Story 2 - Keep secrets out of reach (Priority: P1)

**Goal**: Tokens and webhook secrets never appear in stored records, logs, telemetry, health output, alerts, backups or the dashboard (including error messages from Telegram calls), and every change is recorded without values.

**Independent Test**: Save a token and webhook secret, then inspect the database, logs, telemetry, health output and page content; neither value appears anywhere.

### Tests for User Story 2

- [X] T048 [P] [US2] Add a persistence test that a document saved through the provider with a real `DataProtectionProvider` contains neither the token nor the webhook secret in `tests/BotFarm.IntegrationTests/Persistence/BotSettingsEncryptionTests.cs`.
- [X] T049 [P] [US2] Add audit log tests: accepted saves log bot, changed setting names and administrator at information level; rejections log the category at warning level; failures are logged with the exception; no log entry or exception passed to the logger contains the token or the webhook secret, in `tests/BotFarm.Core.UnitTests/Services/BotSettingsAuditLogTests.cs`.
- [X] T050 [US2] Add render tests that the card and dialog markup never contain a stored or just-saved token or webhook secret in `tests/BotFarm.Shared.UnitTests/Components/BotSettingsDialogTests.cs` (after T036, which creates the file).
- [X] T051 [P] [US2] Add a test that backing up and restoring TestBot leaves its `BotSettings` document unchanged and the archive contains no settings in `tests/BotFarm.IntegrationTests/Backups/MongoDbBackupServiceTests.cs`.
- [X] T052 [P] [US2] Add a host test that saves a token and webhook secret for TestBot, enables it, then checks that the `/health` response and the metrics and logs exported to the OTLP receiver (as in `TelemetryExportTests`) contain neither value, in `tests/BotFarm.IntegrationTests/Host/BotSettingsSecretExposureTests.cs`.
- [X] T053 [P] [US2] Add tests that a Telegram client failure (`RequestException` or a subclass) while loading chats, broadcasting or sending a message shows a localized error without the exception text, and is logged with the bot name, `ApiRequestException.ErrorCode` and the exception, in `tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs` and `tests/BotFarm.Shared.UnitTests/Components/SendMessageDialogTests.cs`.

### Implementation for User Story 2

- [X] T054 [US2] Add the audit logs from contracts/bot-settings.md (accepted, rejected; names, category, administrator; failures with the exception and `ErrorCode`) to the save path in `BotFarm.Core/Services/BotControlCoordinator.cs`.
- [X] T055 [US2] Store `updatedBy` and `updatedAtUtc` on every save and show them in the view in `BotFarm.Core/Services/BotSettingsProvider.cs`.
- [X] T056 [P] [US2] Catch Telegram client exceptions (`RequestException` and subclasses) separately in `BotFarm.Shared/Components/DashboardChats.razor.cs` (chat loading and broadcast) and `BotFarm.Shared/Components/SendMessageDialog.razor.cs`: log the bot name, `ErrorCode` and the exception, and show localized "Telegram rejected the request" (with the error code) and "Telegram could not be reached" messages without exception text; add the strings to `BotFarm.Shared/AdminLanguages/BotFarm.Shared/en-US.json`, `BotFarm.Shared/AdminLanguages/BotFarm.Shared/ru-RU.json` and `BotFarm.Shared/AdminLanguages/BotFarm.Shared/uk-UA.json`.

**Checkpoint**: Quickstart section 3 finds no secret anywhere.

---

## Phase 5: User Story 3 - Upgrade with saved bot states (Priority: P2)

**Goal**: Configuration no longer controls settings or enabled state, and bots keep their saved enabled state once their settings are entered.

**Independent Test**: Start with bot settings only in configuration and no stored settings; the bots are "Not configured", and entering a bot's settings returns it to its saved state.

### Tests for User Story 3

- [X] T057 [P] [US3] Add a host registration test that bot settings in configuration are not read, in `tests/BotFarm.UnitTests/WebApplicationExtensionsTests.cs`.
- [X] T058 [P] [US3] Add a host test that a bot without settings is gated until it is configured and enabled, in `tests/BotFarm.IntegrationTests/Host/BotSettingsStartupTests.cs`.

**Checkpoint**: Quickstart section 4 passes.

---

## Phase 6: User Story 4 - Recover from unreadable settings (Priority: P3)

**Goal**: A bot whose stored secrets cannot be decrypted is marked, gated and fixable from the dashboard without affecting other bots.

**Independent Test**: Replace the key ring, restart, confirm the affected bot shows "Not configured" with "Token must be re-entered" on its settings card and cannot be enabled, then re-enter the token and confirm it returns to its saved state.

### Tests for User Story 4

- [X] T064 [P] [US4] Add provider tests: an unprotect failure for the token or webhook secret gives `Unreadable` for that bot only, other bots load, and one error log names the setting, in `tests/BotFarm.Core.UnitTests/Services/BotSettingsProviderTests.cs`.
- [X] T065 [US4] Add coordinator tests: an `Unreadable` bot reports `NotConfigured` and rejects enabling; saving a new token (and a new or cleared webhook secret when that was unreadable) restores it and applies its saved desired state, in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorSettingsTests.cs` (after T034, which creates the file).
- [X] T066 [US4] Add card and dialog tests: the card shows "Token must be re-entered" for `Unreadable` settings and the dialog requires the unreadable values, in `tests/BotFarm.Shared.UnitTests/Components/DashboardBotSettingsTests.cs` (after T035, which creates the file).

### Implementation for User Story 4

- [X] T067 [US4] Catch `CryptographicException` per secret during load, mark the bot `Unreadable`, keep the readable fields in the view and log the setting name, in `BotFarm.Core/Services/BotSettingsProvider.cs`.
- [X] T068 [US4] Require the unreadable values in a save and reload the bot after a successful one in `BotFarm.Core/Services/BotControlCoordinator.cs`.
- [X] T069 [US4] Show "Token must be re-entered" and the required fields in `BotFarm.Shared/Components/DashboardBotSettings.razor` and `BotFarm.Shared/Components/BotSettingsDialog.razor`, with strings in `BotFarm.Shared/AdminLanguages/BotFarm.Shared/en-US.json`, `BotFarm.Shared/AdminLanguages/BotFarm.Shared/ru-RU.json` and `BotFarm.Shared/AdminLanguages/BotFarm.Shared/uk-UA.json`.

**Checkpoint**: Quickstart section 5 passes.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T070 [P] Remove `Enabled` and the placeholder `Token`, `AdminChatId` and `WebhookSecret` values from the `Bots` section in `BotFarm/appsettings.json`, and remove `Enabled` from `BotFarm/appsettings.Development.json`.
- [X] T071 [P] Stop writing `Enabled` and placeholder settings for new bots and replace the next-step text with configuring and enabling the bot from the dashboard in `scripts/new-bot.sh` and `scripts/new-bot.ps1`.
- [X] T072 Update `README.md`: Getting Started (configure and enable bots from the dashboard), Configuration (`Bots` section no longer read, upgrade steps, no `Enabled`), Data folder (the key ring decrypts stored secrets; losing it means re-entering tokens), rollback (keep settings in configuration until the upgrade is confirmed), and Creating a New Bot steps 4 and 10 (`AddBotConfig(botName)`, no configuration needed).
- [X] T073 Run every test project from [quickstart.md](./quickstart.md) section 1 and fix failures.
- [ ] T074 Run the manual scenarios in [quickstart.md](./quickstart.md) sections 2-6 and record any gaps.
- [X] T075 [P] Add tests that a real `TelegramBotClient` with a fake token never puts the token into its exceptions (refused connection, timeout, HTML error page, malformed JSON, Telegram error answers), with a control case where a handler names the request URL, and that `TelegramBotClientFactory` does not take an `IHttpClientFactory`, in `tests/BotFarm.Core.UnitTests/Services/TelegramClientExceptionTests.cs`.
- [X] T076 [P] Limit the emoji to one visible character of at most 16 UTF-16 code units: `BotConfigValidator.IsValidEmoji`, the `InvalidEmoji` rejection on save and its translations, with tests in `tests/BotFarm.Core.UnitTests/Services/BotConfigValidatorTests.cs` and `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorSettingsTests.cs`.
- [X] T077 [P] Add `BotEvent.SettingsChanged` (tag `settings_changed`) in `BotFarm.Core/Telemetry/BotEventMetrics.cs`, record it in `BotControlCoordinator.SaveAsync` once a save that changes at least one setting is stored, and add the "Settings changed" annotation to `monitoring/grafana/dashboards/botfarm-dashboard.json`, with tests in `tests/BotFarm.Core.UnitTests/Telemetry/BotEventMetricsTests.cs` and `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorSettingsTests.cs`.
- [X] T078 [P] Choose the emoji with `emoji-picker-element`: vendor it and its English data in `BotFarm.Shared/wwwroot/lib` (recorded in `BotFarm.Shared/libman.json`), mount it from `BotFarm.Shared/Components/BotSettingsDialog.razor.js`, replace the emoji text field with Choose and Clear in `BotFarm.Shared/Components/BotSettingsDialog.razor(.cs)`, theme it in `BotFarm/wwwroot/css/site.css`, add the strings to the three translation files, with tests in `tests/BotFarm.Shared.UnitTests/Components/BotSettingsDialogTests.cs`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks every user story.
- **User Stories (Phases 3-6)**: Depend on Foundational.
- **Polish (Phase 7)**: T070-T072 and T075-T078 can start after Foundational; T073-T074 need all stories.

### User Story Dependencies

- **US1 (P1)**: Starts after Foundational. No dependency on other stories.
- **US2 (P1)**: Starts after US1, because it hardens and audits the save path and dialog US1 adds.
- **US3 (P2)**: Starts after Foundational.
- **US4 (P3)**: Starts after Foundational for T064 and T067; T065 extends the save tests from T034, T066 extends the card tests from T035, and T068 and T069 need the save path, card and dialog from US1 (T041-T043).

### Within Each Phase

- Tests are written first and must fail before implementation.
- Models and abstractions before services; services before components; components before translations.
- A task that adds tests to a file another task creates (T050, T065, T066) runs after that task.
- `BotControlCoordinator.cs`, `BotSettingsProvider.cs` and the three translation files (T047, T056, T069) are touched by several tasks; those tasks run one at a time.

### Parallel Opportunities

- All of Phase 1 (T001-T009).
- Phase 2 tests T010-T018, then T020 and T021 together, T026 and T027 together, and T032 alongside T031.
- US1 tests T033-T039, then T040, T042, T043, T045 and T046 together.
- US2 tests T048, T049, T051, T052 and T053; T050 once T036 is done; then T056 alongside T054 and T055.
- US3 tests T057 and T058, and US4 test T064, alongside US1 work once Foundational is done.
- Polish T070 and T071.

---

## Parallel Example: User Story 1

```text
Task: "Add token verifier tests in tests/BotFarm.Core.UnitTests/Services/TelegramTokenVerifierTests.cs"
Task: "Add save tests in tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorSettingsTests.cs"
Task: "Add settings card tests in tests/BotFarm.Shared.UnitTests/Components/DashboardBotSettingsTests.cs"
Task: "Add dialog tests in tests/BotFarm.Shared.UnitTests/Components/BotSettingsDialogTests.cs"
```

```text
Task: "Implement TelegramTokenVerifier in BotFarm.Core/Services/TelegramTokenVerifier.cs"
Task: "Create the settings card in BotFarm.Shared/Components/DashboardBotSettings.razor"
Task: "Create the edit dialog in BotFarm.Shared/Components/BotSettingsDialog.razor"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 and Phase 2. The application then starts without settings and reads stored settings.
2. Complete Phase 3 (US1).
3. **STOP and VALIDATE**: quickstart section 2.

US1 already stores secrets encrypted (Phase 2), but US2's audit logs and leak checks are needed before deploying to a server with real tokens.

### Incremental Delivery

1. Setup + Foundational: settings come from the store; no bot can be configured yet except through the database.
2. US1: configure from the dashboard.
3. US2: audit and leak checks. Deployable for new installations.
4. US3: upgrade with saved bot states. Required before upgrading an existing installation.
5. US4: key-loss recovery.
6. Polish: configuration files, scripts, README, full validation.

## Notes

- [P] tasks touch different files and have no dependencies on incomplete tasks.
- Doc comments describe purpose and current behavior only; interface comments state the contract, and mechanism notes go in the implementation's remarks.
- Never log or render a token or webhook secret, including in test failure messages.
- Commit only when asked.
