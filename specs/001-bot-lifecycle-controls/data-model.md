# Data Model: Bot Lifecycle Controls

## Storage Boundary

Persist platform-level bot control data in the `BotControlStates` collection in the
`BotFarmControl` MongoDB database. Keep account data in `BotFarmIdentity` and chat and other bot
business data in each bot's existing database. Bot backup and restore operations must not include
or overwrite control state.

## Entities

### BotControlState

One durable document for each registered bot with an accepted desired state.

| Field | Type | Required | Rules |
|---|---|---:|---|
| `botName` / document ID | string | Yes | Exact stable name from the registered bot identity; unique per bot and never derived from mutable display text. |
| `desiredEnabled` | boolean | Yes | The latest accepted operator target. |
| `lastCommandId` | string or null | Yes | Unique identifier for the latest accepted administrator write; null for configuration-seeded/default state. Used to resolve an ambiguous write acknowledgment by reading the document back. |
| `updatedAtUtc` | timestamp | Yes | UTC time at which this desired state was durably written. |

The document identity is unique, so two records cannot control the same registered bot. An
unregistered bot name is not allowed to affect another bot and is not automatically deleted.

### BotRuntimeStatus

Process-local status for the active application instance; it is not a durable source of intent.

| Field | Type | Required | Rules |
|---|---|---:|---|
| `botName` | string | Yes | Matches the registered bot identity. |
| `desiredEnabled` | boolean or unknown | Yes | Loaded from `BotControlState`; unknown until authoritative state is available. |
| `appliedEnabled` | boolean or unknown | Yes | Unknown until startup or a later transition succeeds. |
| `status` | enum | Yes | `Unknown`, `Pending`, `Applied`, or `Error`. |
| `lastAttemptAtUtc` | timestamp or null | Yes | UTC time of the latest startup, toggle, or explicit retry attempt. |
| `lastError` | string or null | Yes | Sanitized operator-readable error; never include bot tokens or credentials. |

`Applied` means local runtime setup and the corresponding Telegram webhook API operation
succeeded for the latest persisted target. It does not assert that future Telegram delivery is
healthy.

## State Transitions

| Trigger | Desired state | Runtime gate | Status |
|---|---|---|---|
| Process starts before authoritative state is loaded | Unknown | Closed | Unknown |
| State loads and needs startup application | Persisted value | Closed | Pending |
| Disable request is durably accepted | Disabled | Closed immediately | Pending until webhook removal succeeds |
| Enable request is durably accepted | Enabled | Remains closed | Pending until initialization and webhook setup succeed |
| Latest desired transition succeeds | Persisted value | Open only if enabled; otherwise closed | Applied |
| State-store read fails | Unknown | Closed | Unknown; an explicit retry can reload the record |
| State-change write cannot be confirmed | Attempted target is shown but remains unaccepted | Closed | Unknown; while the process remains running, explicit retry repeats the write before applying |
| External transition fails after retries | Persisted value | Closed | Error; an explicit retry reapplies the target |
| Application restarts | Persisted value | Closed until read and one-time startup application succeed | Pending after load |

Explicit enable/disable commands are used; the UI does not send an ambiguous “invert current
value” operation. A new command is accepted only from `Applied`; `Unknown`, `Pending`, and
`Error` states keep the bot busy. An explicit retry is available for `Unknown` and `Error`. If the
original state write could not be confirmed, retry uses that same in-process command and command
ID, persists it before applying it, and does not substitute an older saved target. Otherwise, retry
reloads and reapplies the saved target. An unconfirmed command is not durable and is discarded if
the application restarts before persistence succeeds. An in-process per-bot lock serializes state
writes with Telegram transitions, and only one operation can be in flight for each bot. Startup
and toggle transitions retry inline up to three attempts; after that, no background retry occurs.

## Missing-State Bootstrap Rules

1. For each registered bot, read its control-state record first. An existing record is
   authoritative even when configuration differs.
2. If the read succeeds and no record exists, seed it from `Bots:{botName}:BotConfig:Enabled`.
   Use disabled when that setting is absent or invalid.
3. Insert the seed only if no record exists; never overwrite a record created by an operator or
   another bootstrap attempt.
4. A failed or unavailable database read is not a missing record. Keep the bot state unknown and
   processing gated; configuration must not be used as a fallback. The dashboard's explicit
   retry action can try the read again.
5. Apply these rules whenever bootstrap encounters a missing record, including for later
   registrations. If a record is deleted or the control database is reset, the current
   configuration value will seed the recreated state.

## Validation Rules

- Only currently registered bot identities can be toggled.
- A toggle is accepted only after its desired state is durably acknowledged or read-back confirms
  the operation ID.
- Until the write is confirmed, the attempted target may be shown as an unaccepted in-process
  command; retry must confirm persistence before applying that target.
- A command cannot be accepted while an earlier state is pending or being retried.
- Unknown state never permits bot-specific update processing.
- A disabled state gates incoming processing even if Telegram webhook deletion fails.
