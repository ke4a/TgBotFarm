# Quickstart: Validate Bot Lifecycle Controls

## Prerequisites

- .NET 10 SDK and the repository's restored solution dependencies.
- An isolated MongoDB instance for local persistence checks.
- Use the existing `ConnectionStrings:MongoDb` connection; lifecycle-control records are stored in
  the separate `BotFarmControl` database, while web Identity remains in `BotFarmIdentity`.
- For manual Telegram integration only, an isolated staging bot and a valid public HTTPS webhook
  endpoint. Do not use production bot credentials for local or routine automated tests.
- Run one application instance, matching the supported deployment scope.

Automated checks must substitute MongoDB and Telegram; no live external service is required for
the routine test suite.

## Automated Validation

Run the solution tests:

```sh
dotnet test BotFarm.sln
```

For a fast feature-focused pass:

```sh
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj --no-restore --filter FullyQualifiedName~BotControlCoordinatorTests
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj --no-restore --filter FullyQualifiedName~BotControlCoordinatorInitializationTests
dotnet test tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj --no-restore --filter FullyQualifiedName~BotWebhookInitializerServiceTests
dotnet test tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj --no-restore --filter FullyQualifiedName~DashboardBotControlTests
dotnet test tests/TestBot.UnitTests/TestBot.UnitTests.csproj --no-restore --filter FullyQualifiedName~UpdateControllerTests
```

The feature-specific tests should cover:

- Initial state seeding from configured values, disabled defaults when values are absent/invalid,
  saved-state precedence, and idempotent restart.
- Durable intent, ambiguous write resolution, and rejection of
  commands while a bot is pending or failed.
- Startup with an unavailable state store: dashboard host remains available, bot state is
  unknown, no external webhook changes are issued, and inbound processing is blocked.
- Enable/disable transitions, up to three inline attempts for failed external calls, explicit
  retry after failure, retry of an unconfirmed state-store write, cancellation, and rejection of
  further commands until the current target is applied.
- Inbound requests while enabled, disabled, unknown, and enable-pending; disabled/unknown cases
  must not invoke bot-specific handlers and must return HTTP 503.
- Dashboard rendering and interaction for applied, pending, unknown, error, save-failure, and
  explicit retry states.
- Explicit outbound admin/system messaging remains available when disabled.

## Manual Staging Scenarios

1. Start with an empty control-state store and configured values for the registered bots. Confirm
   each bot is seeded from its configuration and a bot without a valid value defaults disabled.
2. Change one bot's state in the dashboard. Confirm it becomes applied without restarting the
   application, then send an update to verify enabled processing and HTTP 503/no handler invocation
   while disabled.
3. Change a bot's configuration value and restart. Confirm its saved database state remains
   authoritative.
4. Register a new test bot with `Enabled=true` and confirm it is seeded enabled; omit the setting
   and confirm the bot defaults disabled.
5. In a test environment, make the external webhook operation fail. Confirm the coordinator makes
   up to three attempts within the toggle request, the desired state remains visible as `Error`,
   disabled processing stays gated, and another toggle is rejected. Restore the service and use
   the dashboard's explicit Retry action to apply the persisted target.
6. Start with the bot enabled, then make the state store unavailable and request disable. Confirm
   the UI keeps the requested disabled target visible as unconfirmed and processing stays gated.
   Restore the store and use Retry; confirm it persists and applies disabled rather than reapplying
   the older saved enabled target.
7. Make the state store unavailable during startup. Confirm the host serves the authenticated
   dashboard in degraded state, bot states show unknown, no bot updates are processed, and
   webhook configuration is not changed. Restore the store and use the dashboard's Retry action
   to load and apply saved state. Confirm no background retries occur.
8. Verify explicit administrator/system outbound sends remain allowed for a disabled bot.

## Deployment Caution

Keep `Enabled` configuration values as defaults for bots without a control-state record. Existing
database records remain authoritative. A database read failure must not trigger this fallback.
After an administrator changes a bot state, roll forward only; the pre-feature application
version ignores persisted control state.
