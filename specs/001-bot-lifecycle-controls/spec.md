# Feature Specification: Bot Lifecycle Controls

**Feature Branch**: `[master]`

**Created**: 2026-10-05

**Status**: Draft

**Input**: User description: "Allow administrators to enable and disable registered bots from the admin dashboard. Preserve each existing bot's current enabled state once during transition, apply future changes without restarting the application, and report whether each change has taken effect."

## User Scenarios & Testing

### User Story 1 - Toggle a registered bot (Priority: P1)

As an authenticated dashboard administrator, I want to enable or disable a registered bot so I can control whether it processes incoming updates without editing configuration files or restarting the application.

**Why this priority**: This is the primary operator outcome and must work before persistence and recovery behavior can deliver value.

**Independent Test**: Toggle one registered bot in the dashboard and verify its displayed state and incoming-update behavior change without restarting the application.

**Acceptance Scenarios**:

1. **Given** a bot is active, **When** an administrator disables it, **Then** the dashboard records the desired disabled state, stops processing newly arriving updates, and shows whether the external delivery change is pending or complete.
2. **Given** a bot is disabled, **When** an update arrives directly at its endpoint, **Then** the update is not processed and the endpoint returns HTTP 503 so the delivery service may retry after re-enablement.
3. **Given** a bot is disabled, **When** an administrator enables it, **Then** it begins processing incoming updates only after its required startup and delivery setup has succeeded.
4. **Given** a bot is disabled, **When** an explicit administrator or system action sends an outbound message, **Then** that action remains allowed.

---

### User Story 2 - Retain bot choices (Priority: P2)

As an authenticated dashboard administrator, I want my bot enablement choices to survive application restarts so that operators do not have to reapply them after a deployment or restart.

**Why this priority**: Reliable controls require the saved choice to remain authoritative beyond the lifetime of one process.

**Independent Test**: Change the state of an existing bot, restart the application, and verify the selected state remains in effect.

**Acceptance Scenarios**:

1. **Given** a registered bot has no saved control state, **When** the system starts, **Then** it adopts that bot's current configured `Enabled` value, defaulting to disabled when the value is absent or invalid.
2. **Given** an administrator has changed a bot's state, **When** the application restarts, **Then** the administrator's saved choice is retained even if the configured value differs.
3. **Given** a bot is registered without a saved state, **When** its configuration has `Enabled=true`, **Then** it starts enabled; when the setting is absent or invalid, it starts disabled.

---

### User Story 3 - Understand and retry incomplete changes (Priority: P3)

As an authenticated dashboard administrator, I want to see when a requested change is pending or failed and retry it explicitly, so I can distinguish the requested state from what is currently active.

**Why this priority**: The application cannot atomically change its saved state and the external bot-delivery configuration, so operators need clear status and a straightforward recovery action.

**Independent Test**: Simulate an unavailable state store at startup, a failed state write during a toggle, and an external delivery failure; verify safe behavior, visible status, bounded inline retries, and recovery after an explicit retry.

**Acceptance Scenarios**:

1. **Given** an external delivery change fails, **When** the desired state has been saved, **Then** the coordinator retries up to three times within the operation and the dashboard shows the final applied or failed status.
2. **Given** inline retries are exhausted, **When** an administrator explicitly retries after the external service recovers, **Then** the active state converges to the saved desired state.
3. **Given** the state store is unavailable at startup, **When** the application starts, **Then** the authenticated dashboard remains available in a degraded state, bot states are shown as unknown, incoming updates are blocked, and the system does not apply stale legacy values or retry in the background.
4. **Given** a bot is unknown, pending, or failed, **When** an administrator submits another state change, **Then** the request is rejected; a separate retry action either completes an unconfirmed command or reloads and applies the current saved state.
5. **Given** a bot is enabled and the state store becomes unavailable, **When** an administrator requests disable and retries after the store recovers, **Then** the retry persists and applies that requested disabled state rather than reapplying the older enabled state.

---

### Edge Cases

- If disabling a bot cannot stop external delivery, the local update gate still prevents newly arriving updates from being processed.
- Updates already being processed when a disable request is accepted may finish; no new updates are started after the disabled state is applied locally.
- If enabling a bot fails during setup or external delivery configuration, incoming updates remain blocked and the dashboard reports the incomplete state.
- If the state store cannot confirm a requested change, the dashboard reports failure and does not claim the new desired state was accepted. While the process remains running, an explicit retry preserves that attempted target, confirms its persistence, then applies it. A restart before persistence is confirmed discards the unaccepted in-memory command.
- If administrators attempt another change while a bot is unknown, pending, or failed, the system rejects it. An explicit retry first completes persistence for an unconfirmed command, or otherwise reloads and reapplies the persisted target.
- Any registered bot without a saved state is initialized from its configured `Enabled` value, defaulting to disabled when absent or invalid.
- State belonging to a bot that is not currently registered does not cause an unrelated bot to be enabled.

## Requirements

### Functional Requirements

- **FR-001**: The dashboard MUST show each currently registered bot and its desired state, applied state, and pending or failed status when those states differ.
- **FR-002**: Bot state controls MUST use the existing authenticated dashboard access rules.
- **FR-003**: An authenticated dashboard administrator MUST be able to enable or disable each registered bot without restarting the application.
- **FR-004**: The system MUST persist each accepted desired state independently for its registered bot and retain it across application restarts.
- **FR-005**: Whenever a registered bot has no saved state, the system MUST initialize it from its configured `Enabled` value, defaulting to disabled when the value is absent or invalid; an existing saved state MUST take precedence.
- **FR-006**: The system MUST insert an initial state only when no saved state exists and MUST NOT overwrite an administrator's saved choice with configuration.
- **FR-007**: Once a disable request is durably accepted, the system MUST prevent newly arriving updates for that bot from being processed, even if external delivery continues or requests arrive directly.
- **FR-008**: An enable request MUST NOT allow incoming updates to be processed until the bot's required setup and external delivery configuration have succeeded.
- **FR-009**: Disabling a bot MUST NOT prevent explicit administrator- or system-initiated outbound messages.
- **FR-010**: The dashboard MUST distinguish the desired state from whether the requested state is currently applied, and MUST report pending or failed application.
- **FR-011**: The coordinator MUST retry startup and toggle transitions up to three times inline. After those attempts fail, it MUST report `Error` and stop retrying until an explicit retry or a later application startup.
- **FR-012**: If the state store is unavailable at startup, the application MUST keep the authenticated dashboard available in a degraded state, treat bot states as unknown, block incoming update processing, and avoid changing external delivery settings. It MUST NOT retry in the background.
- **FR-013**: The system MUST NOT fall back to stale legacy enabled values when it cannot load authoritative saved state.
- **FR-014**: If a requested state cannot be durably saved, the system MUST report the failure and MUST NOT report that request as accepted. While the process remains running, Retry MUST attempt to persist that same target before applying it.
- **FR-015**: The feature MUST control only bots registered with the application; it MUST NOT add or remove bot registrations.
- **FR-016**: The update endpoint MUST return HTTP 503 without invoking bot-specific handlers while the bot is disabled, unknown, or not yet applied; this permits bounded delivery retries but does not guarantee durable queuing.
- **FR-017**: The system MUST reject state-change requests while a bot is unknown, pending, or failed. The dashboard MUST provide a separate retry action that persists an unconfirmed in-process command before applying it, or reloads and reapplies the saved target when no such command exists.

### Key Entities

- **Registered Bot**: A bot known to the application and available for operator control, identified consistently across application starts.
- **Bot Control State**: The administrator's desired enabled or disabled state for one registered bot, together with whether that state is active, pending, unknown, or failed.

## Success Criteria

### Measurable Outcomes

- **SC-001**: With external services operating normally, an administrator's toggle reaches an applied state without restarting the application.
- **SC-002**: 100% of incoming updates received while a bot is disabled or its state is unknown are prevented from reaching bot-specific processing.
- **SC-003**: 100% of accepted desired states remain unchanged after an application restart and are not reset by the initial-transition value.
- **SC-004**: After an external delivery service recovers, an in-flight bounded retry or an explicit dashboard retry can apply the latest saved desired state without an application restart.
- **SC-005**: For every toggle, the dashboard clearly identifies whether the desired state is enabled or disabled and whether it is applied, pending, unknown, or failed.

## Assumptions

- The existing authenticated dashboard user cohort is the administrator cohort for this release; no separate role-management feature is introduced.
- Bots without a saved control-state record, including later registrations, are seeded from their configured value and default to disabled when that value is absent or invalid.
- A disable request prevents new update processing after it is accepted; it does not cancel work that was already in progress.
- Existing explicit administrator- and system-initiated outbound messaging remains available while a bot is disabled.
- The current deployment uses one application instance. Coordinating runtime state across multiple application instances is out of scope for this release.
- After any administrator changes a bot state, deployments must roll forward; rollback to the pre-feature version is not supported because that version does not honor saved control state.
