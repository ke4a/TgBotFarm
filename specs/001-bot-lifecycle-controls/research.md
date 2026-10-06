# Research: Bot Lifecycle Controls

## Repository Findings

- `BotService` currently snapshots `BotConfig.Enabled` when its singleton is constructed.
- `BotWebhookInitializerService.InitializeAll()` currently chooses to pause or initialize each
  bot once, and `Program.Main` runs it before the web host begins serving requests.
- `BotService.Pause()` reports external deletion failure as `false`; the current startup
  initializer does not use that result to retain an explicit failed/pending state.
- `BotService.Resume()` uses an in-memory webhook URL set by prior initialization. A bot that
  starts disabled has no such URL, so enabling must perform the normal initialization and URL
  resolution path rather than relying on `Resume()` alone.
- The webhook controller currently forwards every received update to its update service.
- Bot registrations are known at build time through `BotIdentity` and named/keyed service
  registrations; this feature does not need to create bot registrations dynamically.
- The application uses MongoDB for Identity in the `BotFarmIdentity` database and separately uses
  one MongoDB database per bot for its business data. Per-bot backups operate on the latter
  databases. Bot lifecycle state uses a dedicated `BotFarmControl` database on the same MongoDB
  deployment.
- The admin dashboard already requires authentication. Bot state controls can reuse that access
  policy; a new role-management feature is not part of this scope.
- Identity and bot-control state share the MongoDB deployment. The host can remain available in
  degraded mode during a state read failure, but new sign-ins may still depend on Identity's
  database being available; verify the degraded dashboard flow with an already authenticated
  session.

## Decisions

### Durable intent and external effects

**Decision**: Treat MongoDB as the source of the operator's desired state and Telegram webhook
configuration as a separate external effect. Persist first; only report a toggle as accepted after
the write is acknowledged. Do not roll back desired state merely because the external request
fails. Retry a transition up to three times within the initiating operation; after exhaustion,
show `Error` and allow an explicit retry. If a requested state write cannot be confirmed, retain
that unaccepted command in process memory and retry persistence with the same command ID before
applying it; otherwise, explicit retry reloads the latest saved state. A restart before the write
is confirmed discards the unaccepted in-memory command.

**Rationale**: A MongoDB write and Telegram API call cannot share a transaction. A process may
stop between them, and a timed-out Telegram call may have taken effect despite the missing
response. Startup reconciliation is therefore required even if the immediate call normally
succeeds.

**Alternatives considered**:

- Apply the Telegram call first and save afterward: rejected because a failed save leaves an
  external change that the application cannot recover from as the durable intent.
- Treat the two operations as atomic: rejected because they belong to independent systems.
- Roll back the database write when Telegram fails: rejected because rollback can fail too and
  would obscure the operator's latest intent.

### MongoDB write and concurrency model

**Decision**: Keep each bot's control state in one document and update it with an explicit
`SetEnabled(true/false)` command. Serialize each bot's state write and external transition with an
in-process lock. Accept a command only when that bot is `Applied`; reject it while state is
`Unknown`, `Pending`, or `Error`. An `Error` remains busy for new commands until an explicit retry
applies the persisted target. If a MongoDB write has an ambiguous outcome, read back its command
ID before claiming success. If the write cannot be confirmed, the explicit retry must preserve the
same attempted target and operation ID until the write succeeds; it must not replace that command
with the older persisted target.

**Rationale**: The deployment runs one application instance, so cross-instance coordination is
out of scope. Rejecting changes until a transition is applied prevents a newer command from
racing an in-flight Telegram call and avoids latest-command-wins handling. MongoDB and Telegram
are still independent systems, so ambiguous writes and retry/recovery behavior remain necessary.

**Alternatives considered**:

- Read-then-toggle (`!currentState`): rejected because concurrent requests can overwrite each
  other or apply an unintended state. The UI should submit an explicit target state.
- Accept a newer target while a transition or retry is pending: rejected because it requires
  stale-operation tracking and ordering logic, which is unnecessary for the single-instance
  deployment.
- A multi-document transaction: rejected because each bot's state is independent and MongoDB
  transactions would add complexity without making the Telegram operation transactional.
- New cross-instance lease/pub-sub coordination: deferred because the deployment currently runs
  one application instance; revisit before scaling out.

### State storage boundary

**Decision**: Add a dedicated control-state collection to a separate `BotFarmControl` database
on the existing MongoDB deployment. Keep account data in
`BotFarmIdentity` and bot-specific chat/business state in each bot's existing database.

**Rationale**: The toggle is platform-level control-plane data needed before bot startup. A
dedicated database preserves clear ownership boundaries between account identities and bot
operations. Keeping control state out of per-bot databases also prevents a bot backup/restore from
restoring an old enabled state. The feature is still in development and has no existing control
records, so missing bot state is seeded directly from configuration; no cross-database copy is
needed.

**Alternatives considered**:

- Store control state in each bot's business database: rejected because startup control state
  would be coupled to per-bot availability and could be reverted by a bot's backup restore.
- Put control data in `BotFarmIdentity`: rejected because it mixes operational state with account
  identities despite their distinct ownership and lifecycle.

### Startup, bootstrap, and failure behavior

**Decision**: Start the web host, then invoke one initialization pass directly through
`BotControlCoordinator`. Store failures leave the bot unknown and gated without failing host
startup or triggering background retries. The dashboard can explicitly retry loading and
applying state. Use a saved database record whenever present. If a successful read finds no
record, seed it from `Bots:{botName}:BotConfig:Enabled`, defaulting to disabled if the setting is
absent or invalid. Use insert-if-absent so bootstrap never overwrites a saved state. Apply this
rule to later registrations as well. A database read failure is not a missing record and must not
fall back to configuration.

**Rationale**: Running one initialization pass after the host starts keeps the dashboard available
while state is unknown and gated. Per-document insert-if-absent operations make initialization
and later registrations idempotent without requiring a global migration marker. Configuration
changes only affect bots whose control-state record is absent.

**Alternatives considered**:

- Fail the whole application startup on a state-store outage: rejected because it removes the
  dashboard needed to observe and retry the degraded condition.
- Fall back to `appsettings` whenever MongoDB is unavailable: rejected because stale values could
  re-enable a bot after an administrator disabled it. Configuration is consulted only after a
  successful read confirms that the bot has no state record.

### Update handling and external delivery

**Decision**: Gate the shared update-processing path on a known, applied-enabled runtime state.
For a known-disabled, unknown, or enable-pending bot, return HTTP 503 without invoking bot
handlers. Do not request deletion of queued Telegram updates by default.

**Rationale**: The local gate remains effective if Telegram continues delivering during a failed
webhook deletion or if a request reaches the endpoint directly. Returning a retryable response
while disabled gives Telegram an opportunity to redeliver after re-enablement. Telegram's delivery
retry behavior is finite and is not a durable queue guarantee, so this does not promise that every
update will be retained.

When disabling, a false pause result is a failed reconciliation, not success. Keep local inbound
processing closed and retry webhook deletion; do not make the UI imply external delivery has
stopped.

**Alternatives considered**:

- Rely only on `deleteWebhook`: rejected because the public update endpoint could still receive
  direct, in-flight, or retrying requests.
- Acknowledge disabled or unknown updates as successful: rejected because it discards an update
  delivered during the disabled interval instead of giving the delivery service a chance to retry.
- Drop every queued update when disabling: rejected because the user did not request data loss
  and the existing pause behavior does not explicitly discard pending updates.

### Runtime status and retries

**Decision**: Keep `Unknown`, `Pending`, `Applied`, and `Error` runtime status separate from the
persisted desired enabled value. Record the last successful applied enabled value and a sanitized
last error in process-local state. The coordinator owns one-time startup initialization,
per-bot operation serialization, and up to three inline retries for transitions. The dashboard
offers an explicit retry after an operation fails; there is no continuously running reconciliation
worker or idle polling.

**Rationale**: Telegram API success confirms a request was accepted, not that every future update
will be delivered. The UI must not present desired state as proof of ongoing delivery health.
Keeping transient status process-local avoids persisting stale error/apply claims across restarts;
the new process starts pending and reconstructs status by reconciling durable intent.

**Alternatives considered**:

- Persist a single `Enabled` flag and infer runtime state from it: rejected because it cannot
  represent a failed or not-yet-applied external transition.
- Keep a hosted retry worker: rejected because this infrequently used control does not need
  unattended retries; bounded inline retries and an explicit dashboard action provide recovery.

## External References

- MongoDB, [Atomicity and Transactions](https://www.mongodb.com/docs/manual/core/write-operations-atomicity/):
  single-document updates are atomic; update predicates can detect concurrent changes.
- MongoDB, [Retryable Writes](https://www.mongodb.com/docs/manual/core/retryable-writes/):
  driver/server retryable writes cover a bounded set of transient write failures, not an
  end-to-end workflow with Telegram.
- Telegram Bot API, [`setWebhook`](https://core.telegram.org/bots/api#setwebhook),
  [`deleteWebhook`](https://core.telegram.org/bots/api#deletewebhook), and
  [`getWebhookInfo`](https://core.telegram.org/bots/api#getwebhookinfo): webhook configuration
  and delivery diagnostics are external state; pending-update deletion is an explicit option and
  should not be requested by default.
- Telegram Bot API, [Getting updates](https://core.telegram.org/bots/api#getting-updates):
  webhook retries do not constitute durable storage for updates.
