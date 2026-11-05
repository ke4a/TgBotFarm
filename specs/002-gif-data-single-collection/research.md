# Research: Store TestBot GIF Data in One Collection, and Follow Chat ID Changes in All Bots

**Feature**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Date**: 2026-11-05

## R1. GIF record identity and uniqueness

**Decision**: Store GIF records in one collection named `GifData`. The `_id` is a compound key document `{ ChatId, UserId }`, both `long`.

**Rationale**:
- MongoDB always creates the unique `_id` index, and restore rebuilds it, because restore creates collections and inserts documents. A separately created unique index would be lost on every restore (FR-004), since backups carry documents only ([MongoDbBackupService.cs](../../BotFarm.Core/Services/MongoDbBackupService.cs)).
- Looking up a user's last GIF becomes an exact `_id` match: one index probe, whatever the collection size (FR-006).
- This mirrors `ChatSettings`, where the natural key is also the `_id`.

**Alternatives considered**:
- *Generated `_id` plus a unique index on `(ChatId, UserId)`*: the uniqueness rule disappears after a restore unless every restore path recreates the index. Rejected.
- *String `_id` such as `"-100123:42"`*: needs no secondary index for chat-wide queries (anchored prefix regex), but loses typed fields and makes parsing part of the contract. Rejected.

## R2. Chat-wide queries (clear chat, migrate chat) without a secondary index

**Decision**: Select all records of a chat with a range on `_id`:
`_id >= { ChatId: c, UserId: MinKey }` and `_id <= { ChatId: c, UserId: MaxKey }`.
Build it in one private helper in `TestBotDatabaseService` with a doc comment that explains it.

**Rationale**: MongoDB compares embedded documents field by field in field order. `MinKey` and `MaxKey` sort below and above every value. So the range covers exactly the records of chat `c` and is served by the `_id` index, which always exists and survives restore. This works because the key class always serializes `ChatId` before `UserId`.

**Alternatives considered**:
- *Filter on `_id.ChatId` with a secondary index*: the index is lost on restore and needs a startup hook to create. TestBot has no async startup hook for its database service. Rejected for the reference bot.
- *Filter on `_id.ChatId` with no index*: a collection scan for every clear and migrate. That's acceptable at TestBot scale, but bots copied from TestBot would inherit it. Rejected.

## R3. Concurrent saves for the same chat and user

**Decision**: Save with `ReplaceOne(filter: _id == key, IsUpsert = true)`. If the server still reports a duplicate-key write error, retry the replace once, since by then the document exists.

**Rationale**: Two upserts racing on the same `_id` can make one insert fail with a duplicate key. MongoDB 4.2+ retries such upserts on the server when the filter is an equality on the unique key. The single client-side retry covers older servers and keeps FR-005 ("saves never fail") without relying on server behavior. The last write wins.

**Alternatives considered**: `FindOneAndReplace` (same race, no benefit); a transaction (overkill for one document).

## R4. Detecting a chat migration

**Decision**: Add a branch to `UpdateService<TChatSettings>.ProcessUpdate` ([UpdateService.cs](../../BotFarm.Core/Base/UpdateService.cs)) for message updates whose type is a migration message. It normalizes them to `(fromChatId, toChatId)`:

| `Message.Type` | from | to |
|---|---|---|
| `MessageType.MigrateToChatId` (sent in the old group) | `message.Chat.Id` | `message.MigrateToChatId` |
| `MessageType.MigrateFromChatId` (sent in the new supergroup) | `message.MigrateFromChatId` | `message.Chat.Id` |

The branch calls a protected virtual `OnChatMigrated(from, to)`, which by default calls `DatabaseService.MigrateChat(from, to)`.

**Rationale**:
- Every bot inherits `UpdateService<TChatSettings>`, so every bot handles migrations with no per-bot code (FR-011).
- The webhook is registered without an `allowed_updates` filter ([BotService.cs](../../BotFarm.Core/Base/BotService.cs)), and Telegram delivers service messages even in privacy mode, so both messages already reach every bot.
- Handling both messages makes them a natural retry pair (FR-015).

Sources: the Bot API `Message` object documents `migrate_to_chat_id` and `migrate_from_chat_id` (https://core.telegram.org/bots/api#message). The upgrade flow is described at https://core.telegram.org/api/channel.

**Alternatives considered**:
- *Detect from API errors that carry `ResponseParameters.MigrateToChatId`*: useful as a later fallback for missed notices. It's not needed for the spec and would touch every outbound call. Deferred.
- *Bots handle the message in their own `OnUpdate`*: breaks the "every bot, no code" requirement. Rejected.

## R5. Where the move logic lives, and how bots plug in their own data

**Decision**:
- Add `Task MigrateChat(long fromChatId, long toChatId)` to `IDatabaseService`.
- `MongoDbDatabaseService` implements it as a non-virtual template method:
  1. Validate the arguments. A no-op when `from == to`.
  2. Log the start.
  3. Move chat settings through `MongoChatSettingsRepository.MoveChatSettings`.
  4. Call `protected virtual Task MigrateBotChatData(long fromChatId, long toChatId)`. The default does nothing.
  5. Log each owner's outcome.
- TestBot overrides `MigrateBotChatData` to move its GIF records.

**Rationale**:
- Satisfies FR-012 and FR-013 with the smallest surface: one interface method, one overridable hook, and no new DI registrations.
- Bots that store nothing extra get settings migration for free.
- Settings move first. A failure in the bot step does not undo it (Story 3, scenario 6).
- The method stays inside the bot's own database service, so data never crosses the per-bot database boundary (Constitution IV, FR-012).

**Alternatives considered**:
- *Keyed `IChatMigrationHandler` services resolved by `UpdateService`*: more wiring and a second place to look, with no benefit while each bot owns exactly one database service. Rejected for now.
- *Declarative generic mover (bots list per-chat collections, Core moves them)*: discussed and deferred. The template method can host it later without changing callers.

## R6. Moving chat settings safely

**Decision**: `MoveChatSettings(from, to)` works on the raw `ChatSettings` collection as `BsonDocument`, so derived settings types keep all their fields:
1. Read the document with `_id == from`. If none, the outcome is `NotFound`.
2. Copy it, set `_id = to`, and `InsertOne`. On a duplicate-key error, a document under `to` already exists, so the outcome is `KeptExisting`.
3. `DeleteOne(_id == from)`. The outcome is `Moved` or `KeptExisting`.
4. Remove the cache entries `{bot}|ChatSettings|{from}`, `{bot}|ChatSettings|{to}` and `{bot}|ChatSettings|GetAllChatIds`.

**Rationale**:
- `_id` can't be updated in place.
- Insert-then-delete never loses data. A crash between the steps leaves both documents, and a rerun (or the second notice) finds the target present and deletes the source.
- Duplicate-key-as-conflict also resolves the race between the two notices.
- Cache keys follow the existing format in [MongoChatSettingsRepository.cs](../../BotFarm.Core/Services/MongoChatSettingsRepository.cs) (FR-016).

**Conflict rule**: FR-014 says data under the new ID wins.

> ⚠️ **Open product decision.** `GetChatLanguage` creates default settings for any chat it doesn't know ([MongoChatSettingsRepository.cs](../../BotFarm.Core/Services/MongoChatSettingsRepository.cs)). If a command runs in the new supergroup before the migration is handled, the new ID holds defaults, and "new wins" throws away the chat's chosen language. Making old-wins the rule for settings would be a small change: step 2 becomes a replace-upsert instead of an insert. The plan follows the spec (new wins) until the spec changes.

## R7. Moving GIF records

**Decision**: Override `MigrateBotChatData` in `TestBotDatabaseService`:
1. Read records in the R2 range for `from`.
2. For each record, upsert `{ _id: { ChatId: to, UserId }, FileId }` with `$setOnInsert`. An existing record under `to` stays untouched (FR-014).
3. Delete the R2 range for `from`.

Batch the upserts as an unordered bulk write.

**Rationale**: Safe to repeat, because a rerun after step 2 just repeats no-op upserts and the delete. Conflict-safe for the same reason. One round trip per step.

## R8. Error reporting

**Decision**:
- `UpdateService` logs the migration at Information level with bot name, from and to IDs, and each owner's outcome (FR-017).
- On failure it logs an Error with both IDs and rethrows.
- [UpdateControllerBase.cs](../../BotFarm.Core/Controllers/UpdateControllerBase.cs) already turns exceptions into an operator alert through `INotificationService`. It also asks Telegram to redeliver on transient errors, which gives a third retry path.

**Rationale**: Reuses the existing alert and retry pipeline (Constitution V), with no new notification code.

## R9. Leftover per-chat collections

**Decision**: Do nothing with them (FR-009). The new collection name `GifData` cannot clash with numeric chat-ID names. Backup and restore treat `GifData` like any other collection (FR-010). No code changes there.

## R10. Developer documentation

**Decision**: Add a "Store bot data" step to README's "🤖 Creating a New Bot" section (FR-018). It covers:
- one collection per kind of data, keyed by chat (and user) in `_id`;
- the `_id` range helper;
- why unique indexes are not relied on (restore);
- overriding `MigrateBotChatData` for per-chat data.
