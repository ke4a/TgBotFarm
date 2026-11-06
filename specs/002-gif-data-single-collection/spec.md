# Feature Specification: Store TestBot GIF Data in One Collection, and Follow Chat ID Changes in All Bots

**Feature Branch**: `[master]`

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Rewrite TestBot to store its GIF data in one collection instead of one collection per chat. Existing data does not need to be migrated. When a group is upgraded to a supergroup, every bot must move the chat's data to the new chat ID."

**Context**: TestBot currently stores each user's last GIF in a separate collection per chat. Each collection is named after the chat ID and sits in the bot's database next to `ChatSettings`. This feature changes TestBot to store all GIF records in a single store keyed by chat and user, the same way the shared core already stores chat settings. TestBot is a sample bot and the reference that new bots are copied from, so existing GIF data is not carried over. The single-store layout also makes it practical to move a chat's data when Telegram changes its chat ID, which this feature specifies for every bot in the farm (Story 3).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Chat members use GIF features through the new store (Priority: P1)

A chat member sends a GIF in a chat where TestBot is present, and later asks the bot for their last GIF. A chat administrator can clear all of the bot's stored data for their chat. All of this works as it does today: each user's last GIF is remembered per chat and kept separate from other chats, and clearing one chat leaves the others untouched. Behind the scenes, all records now live in one store.

**Why this priority**: This is the whole point of the change. The behavior users see must stay the same.

**Independent Test**: In an empty bot database, send GIFs from two users in two chats, retrieve each user's last GIF in each chat, clear one chat, and confirm that only that chat's records are gone and that all GIF data is in a single store.

**Acceptance Scenarios**:

1. **Given** user A sent GIF X in chat 1 and GIF Y in chat 2, **When** user A requests their last GIF in chat 1, **Then** the bot returns GIF X.
2. **Given** user A already has a stored GIF in chat 1, **When** user A sends a new GIF Z in chat 1, **Then** later requests in chat 1 return GIF Z, and exactly one record exists for user A in chat 1.
3. **Given** records exist for chats 1 and 2, **When** an administrator clears chat 1's data, **Then** no records remain for chat 1 and every record for chat 2 is unchanged.
4. **Given** user B has never sent a GIF in chat 1, **When** user B requests their last GIF in chat 1, **Then** the bot gives the same "nothing stored" response as today.
5. **Given** user A sends two GIFs in chat 1 at almost the same moment, **When** both saves finish, **Then** exactly one record exists for user A in chat 1, it holds one of the two GIFs, and neither save fails.
6. **Given** GIFs have been stored in any number of chats, **When** an operator lists the bot's collections, **Then** there is exactly one collection for GIF data, and no collection is named after a chat ID.

---

### User Story 2 - Backups keep GIF data and its rules (Priority: P2)

An operator backs up TestBot and later restores that backup from the admin dashboard. After the restore, the GIF data matches the backup, and the one-record-per-chat-and-user rule still holds.

**Why this priority**: Backups only store documents, not index definitions. A design that relies on a separately created index would silently lose its uniqueness guarantee after any restore.

**Independent Test**: Store GIFs, back up, change and add records, restore the backup, then confirm the data matches the backup and that concurrent saves for the same chat and user still produce one record.

**Acceptance Scenarios**:

1. **Given** a backup taken after this change, **When** it is restored, **Then** the GIF data matches the backup exactly.
2. **Given** a restore has completed, **When** two saves for the same chat and user arrive at the same moment, **Then** exactly one record exists for that chat and user afterwards.

---

### User Story 3 - Every bot keeps a chat's data when the group is upgraded to a supergroup (Priority: P3)

When a Telegram group becomes a supergroup, its chat ID changes. Every bot in the farm that is present in that group moves all of the data it holds for the old chat ID to the new one. That covers the shared per-chat settings every bot has (such as the chat's language) and any data the bot stores for itself (for TestBot, the GIF records). Users in the upgraded group keep their settings and stored data without doing anything.

This is a farm-wide capability. The shared core detects the chat-migrated notice and moves the data it owns. Each bot declares how to move its own per-chat data, and the core calls that step for the same notice. A bot that stores no per-chat data of its own needs to do nothing to get its shared settings moved.

**Why this priority**: This is a correctness gap for every bot today. Users can live without it, so it can ship after Stories 1–2.

**Independent Test**: For two registered bots, one being TestBot and one with no bot-specific data, store settings, and for TestBot also GIF records, under chat O. Deliver a "chat migrated from O to S" notice to each bot. Confirm that each bot's settings, and TestBot's GIF records, are now under S and gone from O, and that each bot's database changed only for its own data.

**Acceptance Scenarios**:

1. **Given** a bot holds settings for old chat ID O, **When** that bot receives notice that O has migrated to new chat ID S, **Then** the settings are available under S and none remain under O. This applies to every registered bot, not only TestBot.
2. **Given** TestBot holds GIF records for O, **When** it receives the same notice, **Then** all of O's GIF records are available under S and none remain under O.
3. **Given** S already has data (settings or a GIF record for a user) that also exists under O, **When** the migration is applied, **Then** the data already under S is kept and O's conflicting data is discarded. O's data that does not conflict is still moved.
4. **Given** a bot receives the same migration notice twice (Telegram redelivers it), **When** it handles the second delivery, **Then** no data changes and no error is reported.
5. **Given** two bots are in the same upgraded group, **When** each receives its notice, **Then** each moves only the data in its own database, and the move in one bot neither depends on nor affects the other.
6. **Given** a bot's own move step fails, **When** the migration is applied, **Then** the failure is logged and reported to the operator with both chat IDs, the shared settings move is not undone, and handling the notice again, such as when Telegram redelivers it after a temporary failure, finishes the remaining work.
7. **Given** a bot's settings for O were cached before the migration, **When** the bot next reads settings for S or O, **Then** it sees the data after the move and never a stale cached copy.
8. **Given** a bot reacts to the upgrade with a one-time action, such as a message to the new supergroup, **When** the migration notice is handled, **Then** the action runs once, after the data has moved, and does not run if the move fails.

---

### Edge Cases

- **Clearing a chat with no stored data**: succeeds without changing anything, as it does today.
- **Negative chat IDs** (groups and supergroups): stored and looked up the same way as positive IDs. No naming or parsing depends on the sign.
- **Leftover per-chat collections** from before the change, in a database or a restored older backup: the bot neither reads nor removes them. Operators may delete them by hand.
- **A chat-migrated notice for a chat with no stored data**: completes without changes or errors.
- **A chat-migrated notice for a chat this bot has never seen**: treated the same as a chat with no stored data.

## Requirements *(mandatory)*

### Functional Requirements

**Storage layout (TestBot)**

- **FR-001**: TestBot MUST store all GIF records in a single store in its own database. Each record MUST hold the chat ID, the user ID and the GIF file identifier.
- **FR-002**: TestBot MUST NOT create any collection per chat.
- **FR-003**: The system MUST keep at most one record per chat and user. The database itself MUST enforce this rule, so concurrent saves cannot break it.
- **FR-004**: The one-record-per-chat-and-user rule MUST still hold after a backup is restored. Backups carry documents only, not index definitions.
- **FR-005**: Saving a GIF MUST replace the existing record for that chat and user, or create one if none exists. Saves that race each other for the same chat and user MUST both complete without failing, and the last write wins.
- **FR-006**: Looking up a user's last GIF MUST return the record for exactly that chat and user, or report that none exists. Lookup time MUST NOT grow noticeably as the total number of stored records grows.
- **FR-007**: Clearing a chat's data MUST remove every GIF record for that chat and no records for any other chat.
- **FR-008**: The bot's public behavior (commands, callbacks, messages, permissions) MUST stay the same. Only storage changes.
- **FR-009**: Data stored in the old per-chat layout MUST NOT be read, converted or removed. Starting the bot against a database that still contains such collections MUST NOT fail.
- **FR-010**: Backup and restore MUST capture and restore the single store in the same way as other collections, with no special handling.

**Chat ID changes (all bots)**

- **FR-011**: Every registered bot MUST recognize the chat-migrated notice Telegram posts in the new supergroup and run the data move (FR-012 to FR-016) for it. The notice Telegram posts in the old group is handled like any other message. The shared core MUST give bots one handler for the migration that runs once per handled notice, only after the data move has finished, and never when the move fails. The handler MUST document that the old group no longer accepts messages.
- **FR-012**: On a migration, the shared core MUST move the per-chat data it owns for that bot (chat settings) from the old chat ID to the new one, in that bot's own database only.
- **FR-013**: The shared core MUST give bots a standard way to declare how their own per-chat data moves to a new chat ID, and MUST call it for every migration. Bots that declare nothing MUST still get their shared settings moved. TestBot MUST use this mechanism to move its GIF records.
- **FR-014**: For both shared and bot-specific data, data already stored under the new chat ID MUST win any conflict. Data under the old ID that does not conflict MUST still be moved.
- **FR-015**: Handling a migration MUST be safe to repeat. Repeated or duplicate notices MUST have no further effect, and repeating the handling after a partial failure MUST finish the remaining work.
- **FR-016**: After a migration, cached per-chat data for both the old and the new chat ID MUST be invalidated for that bot, so no stale data is served.
- **FR-017**: Each migration MUST be logged with the bot name, old chat ID, new chat ID and the outcome for each data owner (core and bot). Failures MUST be reported through the existing operator notification channel. Logs MUST NOT contain secrets.

**Reference implementation**

- **FR-018**: Developer-facing documentation for creating a new bot from TestBot MUST describe the single-store pattern as the recommended layout for per-chat, per-user data. It MUST also describe how a bot declares how its per-chat data moves when a chat ID changes (FR-013).

### Key Entities

- **GIF record**: the last GIF a user sent in a chat. Attributes: chat ID (a signed integer; negative for groups), user ID, GIF file identifier. Identified by the (chat ID, user ID) pair, of which at most one exists.
- **Chat migration notice**: a Telegram notification that a chat's ID has changed from an old value to a new one. Telegram posts one in the old group and one in the new supergroup; bots act on the one in the new supergroup. Any bot in the farm can receive it.
- **Chat settings**: shared per-chat data that every bot keeps in its own database (for example, the chat's language), identified by chat ID. It is owned by the shared core and moved by the core on migration.
- **Bot-specific per-chat data**: any data a bot stores for a chat itself (for TestBot, GIF records). It is owned by the bot, which declares how the data moves on migration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After any amount of use, TestBot's database contains exactly 1 collection for GIF data, whatever the number of chats.
- **SC-002**: Saves for the same chat and user that race each other never leave more than one record and never fail. Uniqueness comes from the record's identity, which a restore preserves. Automated tests show that a save colliding with a concurrent insert completes successfully.
- **SC-003**: Restoring a backup taken after this change leaves GIF data that exactly matches that backup's contents.
- **SC-004**: For every registered bot, after a group is upgraded to a supergroup, 100% of that chat's settings, plus TestBot's GIF records, are available under the new chat ID in tested cases, and none remain under the old one.
- **SC-005**: The bot's user-visible behavior shows no regressions: all existing TestBot behavior tests for saving, retrieving and clearing GIF data pass unchanged in intent.

## Assumptions

- **No data migration**: TestBot is a sample bot. Existing GIF data in the per-chat layout, and in backups taken before this change, is not carried over. Users simply have no stored GIF until they send a new one.
- **Scope**: The storage-layout change (Stories 1–2) covers TestBot only. No other bot in this repository stores per-chat collections. Bots copied from TestBot later inherit the new pattern through the reference implementation and FR-018. Chat ID migration (Story 3) applies to every registered bot through the shared core.
- **Chat migration while a bot is disabled or paused**: Telegram does not re-send a migration notice. If a bot is disabled when the notice arrives, or its updates are dropped for any other reason, its data stays under the old chat ID. Recovering that data is out of scope.
- **Chat migration ordering**: A migration notice may arrive before or after other updates for the new chat ID. The bot may create default settings for the new chat before the notice arrives. The "data under the new ID wins" rule (FR-014) resolves that case in favor of what the new chat already has.
- **Conflict rule**: When records conflict on migration, the data already under the new chat ID is treated as current.
- **Telegram behavior**: Telegram sends a chat migration notice to bots in the group when it is upgraded. The bots already receive these updates, or can subscribe to them without changing their permissions.
- **Constitution alignment**: Data stays in each bot's own database (Principle IV). Restore keeps pausing webhooks (Principle IV). Migration failures report through the existing notification channel (Principle V). Tests use substitutes rather than a live database (Principle III).
