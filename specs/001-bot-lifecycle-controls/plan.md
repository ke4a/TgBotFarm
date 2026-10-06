# Implementation Plan: Bot Lifecycle Controls

**Branch**: `001-bot-lifecycle-controls` | **Date**: 2026-10-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-bot-lifecycle-controls/spec.md`

## Summary

Add authenticated dashboard controls for each registered bot. Persist the operator's desired
state centrally, seed bots without a saved state from their effective configuration, and let the
coordinator apply each state to the bot runtime and Telegram webhook. Run one initialization pass
after the web host starts; execute toggles and bounded retries inline with the initiating
operation. Keep inbound processing gated until the desired state is known and the runtime
transition has succeeded. Offer explicit retry after a failed operation, and reject new state
changes while a bot is unknown, pending, or failed.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: Existing MongoDB.Driver 3.12, Telegram.Bot 22.10, ASP.NET Core Blazor
Server, and MudBlazor 9.11; no new runtime package planned

**Storage**: MongoDB. Store platform-level bot control state in a dedicated collection in a
separate `BotFarmControl` database on the existing MongoDB deployment, not in the account
database or a bot's chat-data database.

**Testing**: Existing NUnit 5 test projects with NSubstitute; tests substitute MongoDB, Telegram,
and other deployed services

**Target Platform**: Linux container in production; .NET development environments

**Project Type**: Multi-project ASP.NET Core web application with shared core services, Blazor
dashboard, and separately registered bot implementations

**Performance Goals**: Apply state changes without an application restart; perform at most three
transition attempts per operation, show the final status in the dashboard, and do no idle polling

**Constraints**: MongoDB persistence and Telegram webhook changes are not one atomic operation.
The current deployment is a single application instance. State-store failure must not silently
fall back to stale configuration or allow updates while state is unknown. No rollback to the
pre-feature binary is supported after an operator changes state. Identity uses the same MongoDB
deployment, so degraded dashboard access during a total database outage relies on an existing
authenticated session; fresh sign-in is not guaranteed.

**Scale/Scope**: Control only the bots registered by the application; the current deployment
registers one bot and runs one application instance. Multi-instance coordination is out of scope.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Gate | Design evidence |
|---|---|---|
| I. Preserve Layered Ownership | PASS | Runtime coordination and persistence belong in `BotFarm.Core`; the host registers them; the dashboard presents controls; bot-specific update behavior remains behind the shared update boundary. |
| II. Secure Configuration and Access | PASS | Reuse the existing authenticated dashboard access, do not add credentials to the state record, and do not surface raw provider exceptions or secrets. |
| III. Behavior-Focused Automated Tests | PASS | Add deterministic tests using substitutes for MongoDB and Telegram; do not call live external services in routine tests. |
| IV. Isolate and Protect Persistent Data | PASS | Store control-plane state in the dedicated `BotFarmControl` database. Keep it out of Identity, each bot's business-data database, and bot backup/restore flows. |
| V. Operable and Recoverable Services | PASS | Keep a visible desired/applied distinction, log sanitized failures, use bounded inline retries and an explicit recovery action, and document configuration fallback and roll-forward behavior. |

**Gate result**: PASS. No constitution exception or new package is required.

## Project Structure

### Documentation (this feature)

```text
specs/001-bot-lifecycle-controls/
├── plan.md
├── research.md
├── data-model.md
├── contracts/
│   └── bot-control.md
├── quickstart.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
BotFarm.Core/
├── Abstractions/
│   ├── IBotControlService.cs
│   ├── IBotControlStateStore.cs
│   ├── IBotWebhookInitializer.cs
│   ├── IBotService.cs
│   ├── IUpdateService.cs
│   └── UpdateService.cs
├── Models/
│   ├── BotConfig.cs
│   ├── BotControlState.cs
│   └── BotRuntimeStatus.cs
├── Services/
│   ├── BotControlCoordinator.cs
│   ├── BotWebhookInitializerService.cs
│   ├── BotService.cs
│   └── MongoBotControlStateStore.cs
└── Extensions/ServiceCollectionExtensions.cs

BotFarm/
├── Program.cs
├── Startup.cs
└── Pages/
    ├── Dashboard.razor
    └── Dashboard.razor.cs

TestBot/
├── Controllers/UpdateController.cs
├── Extensions/ServiceCollectionExtensions.cs
└── Services/TestBotUpdateService.cs

tests/
├── BotFarm.Core.UnitTests/Services/
│   ├── BotControlCoordinatorTests.cs
│   ├── BotControlCoordinatorInitializationTests.cs
│   ├── BotWebhookInitializerServiceTests.cs
│   └── MongoBotControlStateStoreTests.cs
├── BotFarm.UnitTests/
│   └── Pages/
│       └── DashboardBotControlTests.cs
└── TestBot.UnitTests/Controllers/
    └── UpdateControllerTests.cs
```

**Structure Decision**: Follow the existing Core/host/shared-dashboard/bot split. Core owns the
state contract and coordinator; the host registers the Mongo-backed store and invokes one-time
coordinator initialization after starting the web host; the dashboard awaits toggle and retry
operations; update gating is enforced at the common update boundary so bot-specific handlers
cannot bypass the disabled/unknown state.

## Design Decisions

1. Persist desired state independently from process-local runtime status. A confirmed database
   write is the acceptance point for an operator request; external API failures leave the desired
   state intact and visible as pending/error.
2. Keep runtime status in memory and reconstruct it on startup. The state store holds the durable
   target, not claims about Telegram's current delivery health.
3. Let `BotControlCoordinator` own startup loading, toggle application, and bounded inline
   retries. The host starts the dashboard before invoking one initialization pass. Each toggle
   request waits for the final result. After three failed transition attempts, the dashboard
   offers an explicit retry; there is no continuously running worker or idle polling.
4. Serialize state writes and external transitions per bot. Accept operator changes only while
   the bot is applied; keep unknown, pending, and error states busy until an explicit retry
   succeeds. If a state write is unconfirmed, retain that unaccepted command in process memory and
   have explicit retry persist it before applying; otherwise retry reloads persisted state. A
   disabled or unknown bot is gated immediately; an enabled bot remains gated until initialization
   and webhook configuration succeed.
5. On bootstrap, use a saved control-state record whenever one exists. For a successfully read
   missing record, seed from `Bots:{botName}:BotConfig:Enabled`, defaulting to disabled when the
   setting is absent or invalid. Insert only if absent; never overwrite a saved state. Apply this
   rule to later registrations too. A database read failure is not a missing record and must not
   fall back to configuration. Keep `Enabled` out of the runtime bot configuration model.
6. A webhook request for a disabled, unknown, or not-yet-ready bot receives HTTP 503 without
   invoking bot handlers. This gives the delivery service an opportunity to retry after the bot is
   enabled or its state becomes known; delivery retries are bounded and are not a durable queue.
7. Keep pending external updates by default; do not request deletion of queued updates as part of
   a disable operation.
8. Treat a failed pause result as reconciliation failure. The current pause method reports
   failures with a boolean; a false result must leave the bot gated and visible as pending/error,
   rather than being treated as a completed disable.

## Constitution Check (Post-Design)

**Result**: PASS. Platform control-plane data is isolated in the dedicated `BotFarmControl`
database; Identity and bot chat/business data remain in their existing databases. The design adds
no runtime dependency, no new authorization model, and no live-service dependency to automated
tests. Startup seeding and explicit retry have an explicit path without a continuously running
reconciliation service.
