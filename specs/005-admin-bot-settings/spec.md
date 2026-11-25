# Feature Specification: Admin-Managed Bot Settings

**Feature Branch**: `[master]`

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "Store each bot's settings (token, admin chat, webhook secret, emoji) in the application database so administrators can configure them from the admin dashboard instead of the settings file on the server. The stored settings reach the application through its regular configuration pipeline, and secrets are encrypted at rest with the application's data-protection keys. Configuration no longer decides whether a bot is enabled: a new bot starts disabled until an administrator enables it."

## User Scenarios & Testing

### User Story 1 - Configure a bot from the dashboard (Priority: P1)

As an authenticated dashboard administrator, I want to enter and change a bot's token, admin chat, webhook secret and emoji in the dashboard, so I can set up and maintain bots without editing files on the server or restarting the application.

**Why this priority**: This is the core outcome: bot settings are managed where the bots are operated, and secrets no longer need to sit in a file on the server.

**Independent Test**: On an installation where a registered bot has no settings, enter a valid token and admin chat in the dashboard, enable the bot, and verify it receives updates and sends admin alerts to that chat, all without restarting the application.

**Acceptance Scenarios**:

1. **Given** a registered bot has no stored settings, **When** the application starts, **Then** it starts normally, shows the bot as "Not configured", and does not allow the bot to be enabled.
2. **Given** a bot is "Not configured", **When** an administrator saves a valid token and a non-zero admin chat, **Then** the settings are stored, the dashboard shows the bot as configured and names the Telegram account the token belongs to, and the bot stays disabled until the administrator enables it.
3. **Given** a bot is enabled, **When** an administrator saves a new admin chat, **Then** the next admin alert for that bot goes to the new chat without a restart.
4. **Given** a bot is enabled, **When** an administrator saves a new token or webhook secret, **Then** the bot is re-applied with the new values without an application restart, and the dashboard reports whether re-applying succeeded.
5. **Given** a bot is disabled, **When** an administrator saves a new token, **Then** the new token is used the next time the bot is enabled.
6. **Given** an administrator enters a token that is malformed, rejected by Telegram, or already used by another registered bot, **When** they save, **Then** the save is rejected with a message naming the problem and the bot keeps its current settings.

---

### User Story 2 - Keep secrets out of reach (Priority: P1)

As the operator of the server, I want bot tokens and webhook secrets to be unreadable in the database, in logs and in the dashboard, so that a leaked database dump, log file or screenshot does not expose control of a bot.

**Why this priority**: Moving secrets into the database is only acceptable if they are no easier to steal there than in the settings file.

**Independent Test**: Save a token and a webhook secret, then inspect the stored records, application logs, telemetry, health output and the dashboard's page content and verify none of them contain either value.

**Acceptance Scenarios**:

1. **Given** a token and webhook secret have been saved, **When** someone reads the stored settings records directly from the database, **Then** neither value can be read from them without the application's protection keys.
2. **Given** a bot has a stored token, **When** an administrator opens its settings, **Then** the dashboard shows only that a token is set, the bot ID part of the token and, when known, the Telegram account name, never the token itself.
3. **Given** settings are saved, rejected or fail to load, **When** the application logs the event, **Then** the log names the bot, the setting and the administrator who made the change, never the secret value.

---

### User Story 3 - Upgrade with saved bot states (Priority: P2)

As an operator upgrading an existing installation, I want each bot to keep its saved enabled or disabled state and to read its settings only from what an administrator saved on the dashboard, so configuration no longer decides how a bot runs.

**Why this priority**: Existing installations must upgrade safely; entering each bot's token and admin chat once on the dashboard is the upgrade step.

**Independent Test**: Start the upgraded application with bot settings still in configuration and no stored settings; verify the bots are shown as "Not configured" and ignore the configuration values, then enter a bot's settings and verify it returns to its saved state.

**Acceptance Scenarios**:

1. **Given** a registered bot has no stored settings, **When** the application starts, **Then** the bot is shown as "Not configured", whatever configuration contains, until an administrator saves its token and admin chat.
2. **Given** a bot has stored settings, **When** configuration provides different values, **Then** the stored settings stay in effect.
3. **Given** a bot already has a saved enabled or disabled state, **When** the upgraded application starts and its settings are entered, **Then** the bot keeps that state.
4. **Given** a newly registered bot has no saved enabled or disabled state, **When** the application starts, **Then** the bot starts disabled, whatever its configuration says, and stays disabled until an administrator enables it.

---

### User Story 4 - Recover from unreadable settings (Priority: P3)

As an operator, I want a bot whose stored secrets can no longer be decrypted (for example after the protection keys were lost or the database was moved to another server) to be clearly marked and fixable from the dashboard, so one broken bot never takes down the application.

**Why this priority**: Key loss is rare but otherwise unrecoverable without manual database work; it must degrade to "re-enter the token", not to a failed startup.

**Independent Test**: Store settings, replace the protection keys, restart, and verify the application starts, the affected bot is shown as needing its secrets re-entered and cannot be enabled, and re-entering the token restores it.

**Acceptance Scenarios**:

1. **Given** a bot's stored token cannot be decrypted, **When** the application starts, **Then** the application and the other bots start normally, and the affected bot is shown as "Not configured" with its settings marked "Token must be re-entered", blocks incoming updates and cannot be enabled.
2. **Given** a bot's settings are marked "Token must be re-entered", **When** an administrator saves a valid token, **Then** the bot becomes configured and can be enabled again.

---

### Edge Cases

- If the settings store is unavailable when the application starts, bots are treated as unknown in the same way as an unavailable bot control state: incoming updates are blocked, the dashboard stays available in a degraded state, and configuration values are not used as a fallback.
- If saving fails because the store is unavailable, the dashboard reports the failure and the bot keeps running with its previous settings.
- If a new token belongs to a different Telegram bot account than the current one, the dashboard asks the administrator to confirm before saving, showing both bot IDs, because the bot's existing chats and data belong to the previous account.
- If re-applying an enabled bot after a token or webhook secret change fails, the new settings stay stored and the bot is reported as failed, with the same retry behavior as other bot state changes.
- Settings changes that re-apply a bot are rejected while that bot is unknown or pending, matching the existing rule for state changes. A bot that failed accepts a new token or webhook secret and is re-applied with it, so a bot that failed because of its token can be repaired.
- Clearing the webhook secret makes the bot use the secret derived from its token, and an enabled bot is re-applied so Telegram receives it.
- The token cannot be cleared; to stop a bot, the administrator disables it.
- Restoring a bot's database backup does not change its stored settings.
- When two administrators save settings for the same bot, the later accepted save wins and the dashboard shows the stored result after each save.

## Requirements

### Functional Requirements

- **FR-001**: The dashboard MUST show, for each registered bot, whether its settings are complete. A bot whose settings are incomplete or unreadable MUST be shown as "Not configured", and its settings MUST say "Token must be re-entered" when they are unreadable. The dashboard MUST also show its admin chat, its emoji, whether a token and a custom webhook secret are set, and the bot ID part of the token. The Telegram account name MUST be shown when a save has just verified the token or the bot has been enabled since the application started; it is not stored.
- **FR-002**: An authenticated dashboard administrator MUST be able to set and change a bot's token, admin chat, webhook secret and emoji, using the existing dashboard access rules.
- **FR-003**: The system MUST never return a stored token or webhook secret to the dashboard, and MUST never write either value to logs, telemetry, health output, error messages or alerts.
- **FR-004**: The system MUST store tokens and webhook secrets encrypted, so that the stored records alone cannot reveal them.
- **FR-005**: The system MUST validate settings when they are saved: a token of the form bot ID, colon, secret; a non-zero admin chat; a webhook secret of 1-256 characters from A-Z, a-z, 0-9, '_' and '-' when one is set; and a single emoji when one is set, where an emoji with a skin tone, a flag or a joined sequence counts as one. The dashboard MUST let the administrator choose the emoji from an emoji picker rather than type it.
- **FR-006**: Before accepting a new token, the system MUST confirm with Telegram that the token is valid. If the token is rejected, or Telegram cannot be reached, the save MUST be rejected and the current token kept.
- **FR-007**: The system MUST reject a token that is already stored for another registered bot.
- **FR-008**: When a new token belongs to a different Telegram bot account than the stored one, the system MUST require the administrator's explicit confirmation before saving.
- **FR-009**: Accepted changes MUST take effect without restarting the application: the emoji and admin chat on their next use; a new token or webhook secret by re-applying the bot when it is enabled, or the next time it is enabled when it is disabled.
- **FR-010**: The application MUST start when any bot's settings are missing, invalid or unreadable. Such a bot MUST block incoming updates and MUST NOT be enabled until its settings are complete and readable.
- **FR-011**: The system MUST NOT read bot settings from configuration. A registered bot without stored settings MUST be shown as "Not configured" until an administrator saves them.
- **FR-012**: The system MUST read each bot's token, admin chat, webhook secret and emoji only from its stored settings.
- **FR-014**: When a stored secret cannot be decrypted, the system MUST show that bot as "Not configured" with its settings marked "Token must be re-entered", without affecting other bots, and accept a newly entered token to restore it.
- **FR-015**: When the settings store cannot be read at startup, the system MUST treat bots as unknown, block incoming updates, keep the dashboard available in a degraded state, and MUST NOT fall back to configuration values.
- **FR-016**: The system MUST log each accepted or rejected settings change with the bot, the names of the changed settings and the administrator who made it.
- **FR-017**: The system MUST reject settings changes that require re-applying a bot while that bot is unknown or pending. A bot that failed MUST accept a new token or webhook secret and be re-applied with it.
- **FR-018**: Restoring a bot's database backup MUST NOT change that bot's stored settings.
- **FR-019**: The setup and configuration documentation MUST describe configuring and enabling bots from the dashboard, upgrading from settings in configuration, and recovering a bot whose token must be re-entered.
- **FR-020**: A registered bot with no saved enabled or disabled state MUST start disabled. Configuration MUST NOT set or change whether a bot is enabled; the saved state an administrator chooses on the dashboard is the only source.

### Key Entities

- **Bot Settings**: The stored settings of one registered bot: its token and optional webhook secret (both secret and encrypted), its admin chat, its optional emoji, and when and by whom they were last changed.
- **Registered Bot**: A bot known to the application, identified by its name; settings belong to exactly one registered bot.
- **Protection Keys**: The application's existing keys that encrypt and decrypt stored secrets; without them, stored secrets cannot be read.

## Success Criteria

### Measurable Outcomes

- **SC-001**: On a fresh installation, an administrator can configure a bot and have it receiving updates in under 2 minutes, without editing files on the server or restarting the application.
- **SC-002**: Saved tokens and webhook secrets appear in 0 places outside the application's memory: not in stored records, logs, telemetry, health output, bot backups or dashboard page content.
- **SC-003**: After upgrading, 100% of bots keep their saved enabled or disabled state once their settings are entered on the dashboard.
- **SC-004**: 100% of accepted settings changes take effect without an application restart.
- **SC-005**: A bot with missing, invalid or unreadable settings never stops the application or any other bot from starting.
- **SC-006**: 100% of newly registered bots start disabled and receive no updates until an administrator enables them.

## Assumptions

- The existing authenticated dashboard user cohort is the administrator cohort; no separate role or permission for settings is introduced.
- Stored settings live in the application's shared control store alongside bot control state, not in each bot's own database, so they are not part of per-bot backups. If that store is lost, the administrator re-enters each bot's token (obtainable again from @BotFather) and admin chat.
- Stored settings reach the application through its existing configuration pipeline, so the components that read bot settings today keep reading them the same way.
- Encryption uses the application's existing data-protection key ring, kept in the data folder. Losing or replacing the key ring makes stored secrets unreadable, which is handled by re-entering them.
- Enabled state keeps being stored and changed as it is today; only its starting value for bots without a saved state changes, to disabled. A bot that has no saved state when the upgraded application first starts therefore starts disabled even if configuration enabled it.
- Bots are still registered in code; adding or removing bots from the dashboard is out of scope.
- The database connection string, the public webhook address and other application-wide settings stay in configuration, because the application needs them before it can read stored settings.
- The current deployment runs one application instance; propagating settings changes across several instances is out of scope.
- After settings have been stored, rolling back to a version without this feature requires putting the settings back into configuration.
