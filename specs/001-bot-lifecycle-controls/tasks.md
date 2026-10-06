---

description: "Task list for Bot Lifecycle Controls"
---

# Tasks: Bot Lifecycle Controls

**Input**: Design documents from `specs/001-bot-lifecycle-controls/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/bot-control.md`

**Tests**: Focused automated tests are required by the project constitution. Use substitutes for MongoDB and Telegram; do not require live external services.

**Organization**: Tasks are grouped by user story. Test tasks precede implementation tasks within each story.

## Phase 1: Setup

**Purpose**: Reuse the existing .NET solution, MongoDB driver, and NUnit test projects. No new project scaffolding or dependencies are required.

## Phase 2: Foundational

**Purpose**: Define the shared control-state and runtime contracts used by the dashboard, host, and bot implementations.

- [X] T001 [P] Create the durable bot desired-state model with stable bot identity, target, command ID, and UTC update time in `BotFarm.Core/Models/BotControlState.cs`.
- [X] T002 [P] Create the process-local runtime status model with unknown, pending, applied, and error states in `BotFarm.Core/Models/BotRuntimeStatus.cs`.
- [X] T003 Define the persistence contract for authoritative state reads/writes and insert-if-absent initial seeds in `BotFarm.Core/Abstractions/IBotControlStateStore.cs`.
- [X] T004 Define the explicit enable/disable and status-query contract in `BotFarm.Core/Abstractions/IBotControlService.cs`.
- [X] T005 Update the bot and webhook-initializer abstractions to support initialization, pause, and readiness reporting for live control in `BotFarm.Core/Abstractions/IBotService.cs` and `BotFarm.Core/Abstractions/IBotWebhookInitializer.cs`.

**Checkpoint**: Shared models and contracts are in place; user-story implementation can begin.

## Phase 3: User Story 1 - Toggle a registered bot (Priority: P1) 🎯 MVP

**Goal**: Let an authenticated dashboard user set a bot's desired state without restarting; gate inbound processing immediately and report whether setup has applied.

**Independent Test**: With a registered bot and a seeded authoritative state record, enable and disable it from the dashboard. Confirm desired/applied status changes without restarting, disabled/unknown/pending inbound requests return HTTP 503 without invoking handlers, and explicit outbound sends remain available.

### Tests for User Story 1

> Write these tests first and confirm they fail before implementing the behavior.

- [X] T006 [P] [US1] Add coordinator tests for explicit targets, persist-before-acceptance, busy-command rejection, immediate disable gating, and outbound-send availability in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs`.
- [X] T007 [P] [US1] Add state-store tests for per-bot persistence, desired-state updates, and operation-ID read-back in `tests/BotFarm.Core.UnitTests/Services/MongoBotControlStateStoreTests.cs`.
- [X] T008 [P] [US1] Add webhook-initializer tests proving enablement opens the gate only after initialization and webhook setup, and disablement closes it immediately, in `tests/BotFarm.Core.UnitTests/Services/BotWebhookInitializerServiceTests.cs`.
- [X] T009 [P] [US1] Add endpoint tests proving enabled updates reach handlers while disabled, unknown, and enable-pending updates return HTTP 503 without handler invocation in `tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs`.
- [X] T010 [P] [US1] Add dashboard page tests for explicit enable/disable actions, pending/unknown/error display, and persistence failures in `tests/BotFarm.UnitTests/Pages/DashboardBotControlTests.cs`.

### Implementation for User Story 1

- [X] T011 [US1] Implement the MongoDB-backed per-bot state store with desired-state writes and ambiguous-write resolution in `BotFarm.Core/Services/MongoBotControlStateStore.cs`.
- [X] T012 [US1] Implement the control coordinator so it persists explicit desired-state commands, rejects commands until the prior target is applied, updates runtime status, and closes the inbound gate on disable in `BotFarm.Core/Services/BotControlCoordinator.cs`.
- [X] T013 [US1] Implement live bot pause and enable transitions; enabling must resolve/configure the webhook before opening the inbound gate in `BotFarm.Core/Services/BotService.cs` and `BotFarm.Core/Services/BotWebhookInitializerService.cs`.
- [X] T014 [US1] Gate the shared inbound update path on known applied-enabled state and return HTTP 503 for disabled, unknown, or enable-pending states without invoking bot handlers in `BotFarm.Core/Abstractions/IUpdateService.cs`, `BotFarm.Core/Services/UpdateService.cs`, and `TestBot/Controllers/UpdateController.cs`.
- [X] T015 [US1] Add inline dashboard controls that submit explicit target states and show per-bot unresolved failures in `BotFarm/Pages/Dashboard.razor` and `BotFarm/Pages/Dashboard.razor.cs`.
- [X] T016 [US1] Place the inline controls on the authenticated dashboard in `BotFarm/Pages/Dashboard.razor`.
- [X] T017 [US1] Register the state store in the dedicated `BotFarmControl` database on the shared MongoDB deployment, register the control service, and wire the bot update boundary in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`, `BotFarm/Startup.cs`, and `TestBot/Extensions/ServiceCollectionExtensions.cs`.

**Checkpoint**: A seeded bot can be toggled live, inbound handlers remain gated until the accepted transition applies, and the dashboard accurately reflects desired versus applied state.

## Phase 4: User Story 2 - Retain bot choices (Priority: P2)

**Goal**: Preserve administrator choices after restarts and initialize any bot without saved state from configuration, defaulting to disabled when no valid value exists.

**Independent Test**: Start with configuration and an empty control store, verify each bot is seeded from its configured value or defaults disabled, change a bot's state, restart, and confirm the saved choice remains authoritative; verify a later registration is seeded from its configuration.

### Tests for User Story 2

> Add the configuration seeding and restart tests before changing bootstrap behavior.

- [X] T018 [US2] Add coordinator initialization tests for configured initial state, disabled defaults for absent/invalid values, database precedence on restart, config fallback for newly missing records, and no config fallback on read failure in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorInitializationTests.cs`.

### Implementation for User Story 2

- [X] T019 [US2] Seed any missing bot state from its effective configuration or disabled default, use insert-if-absent, and preserve every saved database state in `BotFarm.Core/Services/BotControlCoordinator.cs`.
- [X] T020 [US2] Read `Enabled` values directly from the configuration pipeline during bootstrap, then remove `Enabled` from the runtime configuration model in `BotFarm.Core/Models/BotConfig.cs`.
- [X] T021 [US2] Start the web host before invoking a one-time coordinator initialization pass, keeping the dashboard available during state-store failures without registering a background worker in `BotFarm/Program.cs` and `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`.
- [X] T022 [US2] Retain `Enabled` configuration values as defaults for bots without a state record, and confirm a database read failure does not fall back to configuration in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorInitializationTests.cs`.

**Checkpoint**: Saved choices survive restarts; bots without saved state use configured defaults or start disabled when no valid value exists.

## Phase 5: User Story 3 - Understand and recover from incomplete changes (Priority: P3)

**Goal**: Keep the dashboard available during state-store and provider failures, expose actionable pending/error status, and recover through bounded inline retries and an explicit dashboard action.

**Independent Test**: Substitute state-store and Telegram failures during startup and toggles. Confirm the host remains available, unknown/disabled bots stay gated with HTTP 503 responses, status reports failure, inline retries are bounded, and an explicit retry applies the saved target after recovery.

### Tests for User Story 3

> Add deterministic failure/recovery tests before extending retry behavior.

- [X] T023 [P] [US3] Add coordinator tests for transient API failures, failed pause results, three inline attempts, explicit retry, busy-command rejection, sanitized error status, and cancellation in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs`.
- [X] T024 [P] [US3] Add initialization tests proving a state-store outage leaves status unknown, bot processing gated, and webhook settings unchanged; invoke initialization after host start so the dashboard remains available in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorInitializationTests.cs` and `BotFarm/Program.cs`.

### Implementation for User Story 3

- [X] T025 [US3] Move startup loading, inline transition retries, and explicit retry into the coordinator; remove the host-managed retry worker and serialize all operations per bot in `BotFarm.Core/Services/BotControlCoordinator.cs`.
- [X] T026 [US3] Preserve pending/error status and keep the local gate closed when pause or enable operations fail; log sanitized actionable failures and expose a dashboard retry action in `BotFarm.Core/Services/BotControlCoordinator.cs` and `BotFarm/Pages/Dashboard.razor`.
- [X] T027 [US3] Verify the retry/recovery, HTTP 503, initial-state seeding, and roll-forward deployment scenarios are runnable and accurately described in `specs/001-bot-lifecycle-controls/quickstart.md`.

**Checkpoint**: Transient failures remain visible; bounded inline retries and explicit retry recover state without applying stale values or processing updates while runtime state is unsafe.

- [X] T030 [US3] Reject state changes while a bot is unknown, pending, or failed; serialize toggle and retry operations and keep the dashboard switch disabled until applied in `BotFarm.Core/Services/BotControlCoordinator.cs`, `BotFarm/Pages/Dashboard.razor.cs`, and `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs`.
- [X] T031 [US3] Mark runtime state unknown when an accepted command is canceled during persistence, keep the update gate closed, and recover from the durable target through explicit retry in `BotFarm.Core/Services/BotControlCoordinator.cs` and `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs`.

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Validate the complete feature against the focused automated and operational scenarios.

- [X] T028 Run the feature-focused tests listed in `specs/001-bot-lifecycle-controls/quickstart.md` and resolve failures in the corresponding feature source and test files.
- [X] T029 Review the final changes against `specs/001-bot-lifecycle-controls/spec.md`, `specs/001-bot-lifecycle-controls/contracts/bot-control.md`, and `.specify/memory/constitution.md`; confirm no live Telegram or MongoDB dependency was added to routine tests.

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No project or dependency setup is needed.
- **Foundational (Phase 2)**: Complete before all user-story work.
- **User Story 1 (Phase 3)**: Depends on the shared models and contracts in Phase 2.
- **User Story 2 (Phase 4)**: Depends on User Story 1's durable state-store contract and implementation.
- **User Story 3 (Phase 5)**: Depends on User Story 1's runtime coordinator and User Story 2's initial-load/state-seeding path.
- **Polish (Phase 6)**: Depends on all three user stories.

### User Story Dependencies

- **User Story 1 (P1)**: Starts after Phase 2; its independent test uses a seeded authoritative state record.
- **User Story 2 (P2)**: Follows User Story 1 because bootstrap uses its durable state store.
- **User Story 3 (P3)**: Follows User Stories 1 and 2 because retry/recovery reconciles their accepted state and startup seeding.

### Parallel Opportunities

- T001 and T002 can be implemented in parallel because they create independent model files.
- After Phase 2, US1 test tasks T006-T010 can run in parallel because each targets a separate test file.
- US3 test tasks T023 and T024 can run in parallel because they target separate test projects/files.
- Within each story, implementation follows its tests; tasks that edit the same service or test file remain sequential.

## Parallel Example: User Story 1

```text
Task: T006 coordinator behavior tests in tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs
Task: T007 state-store tests in tests/BotFarm.Core.UnitTests/Services/MongoBotControlStateStoreTests.cs
Task: T008 webhook-initializer tests in tests/BotFarm.Core.UnitTests/Services/BotWebhookInitializerServiceTests.cs
Task: T009 update-endpoint tests in tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs
Task: T010 dashboard page tests in tests/BotFarm.UnitTests/Pages/DashboardBotControlTests.cs
```

## Implementation Strategy

### MVP First

Implement Phase 2 and User Story 1 first, using a seeded control-state document to prove live
toggle behavior. Before deploying, complete User Story 2's configuration seeding, saved-state
precedence, and fail-closed behavior checks.

### Incremental Delivery

1. Complete Phase 2 and User Story 1; validate live toggling, local update gating, and dashboard status.
2. Complete User Story 2; verify configuration seeding, restart retention, and disabled defaults when configuration is absent before rollout.
3. Complete User Story 3; verify degraded startup, retries, failure visibility, and recovery.
4. Run Phase 6 validation and follow the roll-forward deployment caution in `specs/001-bot-lifecycle-controls/quickstart.md`.

## Notes

- `[P]` tasks touch distinct files and have no dependency on another incomplete task.
- `[US1]`, `[US2]`, and `[US3]` map to the prioritized stories in `specs/001-bot-lifecycle-controls/spec.md`.
- Keep control-state records in the dedicated `BotFarmControl` database, separate from Identity and per-bot business data.
- A 503 response permits bounded delivery retries; it is not a durable update queue guarantee.
- Keep configured `Enabled` values only as fallbacks for bots without a saved control-state record.
