---

description: "Task list for storing TestBot GIF data in one collection and following chat ID changes in all bots"
---

# Tasks: Store TestBot GIF Data in One Collection, and Follow Chat ID Changes in All Bots

**Input**: Design documents from `/specs/002-gif-data-single-collection/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/chat-data-contracts.md](./contracts/chat-data-contracts.md), [quickstart.md](./quickstart.md)

**Tests**: Included. Constitution Principle III requires focused behavior tests for every change, using NSubstitute substitutes for MongoDB and Telegram (no live services). Within each story, write the tests first and confirm they fail before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

Multi-project .NET solution at the repository root: `BotFarm.Core/`, `TestBot/`, `tests/BotFarm.Core.UnitTests/`, `tests/TestBot.UnitTests/`, `README.md`.

## Conventions for every task

- XML doc comments describe the purpose and current behavior only. They never mention old behavior or fixed bugs.
- Detect duplicate-key writes the way `BotFarm.Core/Services/MongoBotControlStateStore.cs` does: `catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)`.
- In tests, build duplicate-key exceptions like the `CreateWriteException` helper in `tests/BotFarm.Core.UnitTests/Services/MongoBotControlStateStoreTests.cs`. Copy it into the test class that needs it.
- Log with the existing `"{LogPrefix} ..."` structured-logging style, using `Identity.LogPrefix`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Confirm a green baseline before changing anything.

- [X] T001 Run `dotnet build` at the repository root, then `dotnet test tests/BotFarm.Core.UnitTests` and `dotnet test tests/TestBot.UnitTests`. Record any tests that already fail, so they aren't blamed on this feature.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: None. US1 (TestBot storage) and the Core part of US3 (chat migration) touch different files and share no prerequisite code. The only cross-story dependency is the US3 TestBot part (T026–T028), which needs both US1 and the US3 Core tasks; see Dependencies.

**Checkpoint**: The user story phases can start after Phase 1.

---

## Phase 3: User Story 1 - Chat members use GIF features through the new store (Priority: P1) 🎯 MVP

**Goal**: TestBot stores, retrieves and clears GIFs in a single `GifData` collection keyed by `_id: { ChatId, UserId }`. Behavior users see does not change (FR-001 to FR-009).

**Independent Test**: In an empty database, send GIFs from two users in two chats, retrieve each, clear one chat, and confirm only that chat's records are gone and only a `GifData` collection exists ([quickstart.md §2](./quickstart.md#2-manual-check-single-collection-stories-12), steps 1–4).

### Tests for User Story 1 ⚠️ (write first, confirm they fail)

- [X] T002 [US1] Rewrite `tests/TestBot.UnitTests/Services/TestBotDatabaseServiceTests.cs` for the new layout. Remove the per-chat tests (`GetGifData_FindsUserInChatSpecificCollection`, `SaveGifData_ReplacesUserGifWithUpsertEnabled`, `ClearChatData_DropsChatSpecificCollection`). Add:
  - (a) `GetGifData` uses `GetCollection<GifData>("GifData")` and finds by `_id == new GifDataKey { ChatId, UserId }`. Assert the rendered filter.
  - (b) `SaveGifData(chatId, userId, fileId)` calls `ReplaceOneAsync` with that `_id` filter, `IsUpsert = true`, and a document whose `Id` and `FileId` match.
  - (c) When the first `ReplaceOneAsync` throws a duplicate-key `MongoWriteException`, `SaveGifData` retries once and completes. This is the SC-002 evidence.
  - (d) When both attempts throw a duplicate-key error, the exception propagates.
  - (e) `ClearChatData(chatId)` calls `DeleteManyAsync` with the `_id` range filter `{ $gte: { ChatId: c, UserId: MinKey }, $lte: { ChatId: c, UserId: MaxKey } }`. Assert the rendered BSON.
  - (f) `ClearChatData` never calls `DropCollectionAsync`.
  - (g) No method ever calls `GetCollection` with a numeric (chat ID) name.
- [X] T003 [P] [US1] Add a model test in a new `tests/TestBot.UnitTests/Models/GifDataSerializationTests.cs`. Serializing `new GifData { Id = new GifDataKey { ChatId = -100123, UserId = 42 }, FileId = "f" }` to `BsonDocument` gives `{ _id: { ChatId: -100123, UserId: 42 }, FileId: "f" }`, with `ChatId` as the first element of `_id`. Deserializing it gives the same values. This pins the field order that R2 relies on.
- [X] T004 [P] [US1] Update `tests/TestBot.UnitTests/Services/TestBotUpdateServiceTests.cs`:
  - Assert that receiving an animation calls `SaveGifData(message.Chat.Id, message.From.Id, animation.FileId)`.
  - Assert that a save exception is still logged and reported through `INotificationService.SendErrorNotification`.
  - Remove assertions on the old `SaveGifData(long, GifData)` overload.
- [X] T005 [P] [US1] Update `tests/TestBot.UnitTests/Handlers/Commands/GetLastGifCommandHandlerTests.cs` so substitutes return `GifData` built with `Id = new GifDataKey { ... }`. Keep asserting that `SendAnimation` gets `FileId` and that the "NoGifsFound" path is unchanged.

### Implementation for User Story 1

- [X] T006 [US1] In `TestBot/Models/GifData.cs`:
  - Add a `GifDataKey` class with `long ChatId` (`[BsonElement(Order = 1)]`) and `long UserId` (`[BsonElement(Order = 2)]`), declared in that order. Its doc comment says the order is required by chat-wide `_id` range queries.
  - Change `GifData` to `[BsonId] public GifDataKey Id { get; set; }` plus `public string FileId { get; set; }`, and remove the `UserId` property.
  - Update XML docs to describe the single-collection record.
- [X] T007 [US1] In `TestBot/Abstractions/ITestBotDatabaseService.cs`:
  - Change `SaveGifData` to `Task SaveGifData(long chatId, long userId, string fileId)`.
  - Keep `GetGifData(long chatId, long userId)` and `ClearChatData(long chatId)`.
  - Update XML docs ("in the bot's GIF collection").
- [X] T008 [US1] Rewrite `TestBot/Services/TestBotDatabaseService.cs`:
  - Add a private `const string GifCollectionName = "GifData"` and a private `Collection` property returning `Instance.GetCollection<GifData>(GifCollectionName)`.
  - Add a private static `KeyFilter(long chatId, long userId)` returning `_id == new GifDataKey { … }`.
  - Add a private static `ChatRangeFilter(long chatId)` returning the `_id` `$gte`/`$lte` range with `BsonMinKey.Value`/`BsonMaxKey.Value` for `UserId`. Build it as a `BsonDocument` filter. Its doc comment explains why it is index-served and restore-safe (research R2).
  - `GetGifData`: `Find(KeyFilter).FirstOrDefaultAsync()`.
  - `SaveGifData`: `ReplaceOneAsync(KeyFilter, doc, new ReplaceOptions { IsUpsert = true })`, retried once on a duplicate-key error (R3).
  - `ClearChatData`: `DeleteManyAsync(ChatRangeFilter(chatId))`.
- [X] T009 [P] [US1] Update `TestBot/Services/TestBotUpdateService.cs` (the GIF branch around the `new GifData { ... }` block) to call `_databaseService.SaveGifData(message.Chat.Id, message.From.Id, fileId)`. Remove the `GifData` construction, and the `using TestBot.Models;` if it becomes unused.
- [X] T010 [P] [US1] Update `TestBot/Handlers/Commands/GetLastGifCommandHandler.cs` only if it no longer compiles; it reads `lastGif.FileId`, which still exists. Otherwise leave it unchanged.
- [X] T011 [US1] Fix any other compile errors from the model change: run `grep -rn "GifData\|SaveGifData" TestBot tests/TestBot.UnitTests` and update the remaining references, such as `TestBot/Services/TestBotMarkupService.cs` and `tests/TestBot.UnitTests/Services/TestBotMarkupServiceTests.cs` if they reference `GifData.UserId`.
- [X] T012 [US1] Run `dotnet test tests/TestBot.UnitTests`. All tests pass, including T002–T005.

**Checkpoint**: US1 is complete and independently testable. This is the MVP.

---

## Phase 4: User Story 2 - Backups keep GIF data and its rules (Priority: P2)

**Goal**: Backup and restore round-trip the `GifData` collection unchanged, and uniqueness still holds afterwards. That follows from the `_id` key, so this story is verification (FR-004, FR-010; SC-002, SC-003).

**Independent Test**: Back up, change data, restore, then confirm the data matches the backup ([quickstart.md §2](./quickstart.md#2-manual-check-single-collection-stories-12), step 5).

### Tests for User Story 2

- [X] T013 [P] [US2] In `tests/TestBot.UnitTests/Models/GifDataSerializationTests.cs`, add a test that a raw `BsonDocument` shaped like a restored document (`{ _id: { ChatId, UserId }, FileId }`) deserializes into `GifData`. That shows a restored collection is readable without conversion. Depends on T003.
- [X] T014 [P] [US2] Review `tests/BotFarm.Core.UnitTests/Services/MongoDbBackupServiceTests.cs` and `BotFarm.Core/Services/MongoDbBackupService.cs` to confirm that neither special-cases collection names or indexes. If true, make no code change and note "FR-010 satisfied without changes" in the PR or commit message. If not, stop and report.

### Implementation for User Story 2

- [X] T015 [US2] No production code is expected; the design in T006/T008 covers it. Run `dotnet test tests/TestBot.UnitTests` and `dotnet test tests/BotFarm.Core.UnitTests`, and confirm all tests pass.

**Checkpoint**: US1 and US2 both work independently.

---

## Phase 5: User Story 3 - Every bot keeps a chat's data when the group is upgraded to a supergroup (Priority: P3)

**Goal**: Every bot recognizes both Telegram migration messages, and Core moves `ChatSettings` from the old chat ID to the new one. Bots can override a hook to move their own per-chat data, and TestBot moves its GIF records (FR-011 to FR-017).

**Independent Test**: Deliver `MigrateToChatId` and `MigrateFromChatId` updates for chat O → S. Confirm the settings and GIF records are under S only, the second notice changes nothing, and logs carry the bot name and both IDs ([quickstart.md §3](./quickstart.md#3-manual-check-chat-migration-story-3)).

### Tests for User Story 3 (Core) ⚠️ (write first, confirm they fail)

- [X] T016 [P] [US3] In `tests/BotFarm.Core.UnitTests/Services/MongoChatSettingsRepositoryTests.cs`, add tests for `MoveChatSettings(from, to)` against a substituted `IMongoCollection<BsonDocument>` named `ChatSettings`:
  - (a) Nothing under `from` → returns `NotFound`, with no insert or delete.
  - (b) A document under `from` → `InsertOneAsync` gets a copy with `_id = to` and every other field preserved, including derived-type fields; then `DeleteOneAsync(_id == from)`; returns `Moved`.
  - (c) The insert throws a duplicate-key error → `DeleteOneAsync(_id == from)` is still called, and the method returns `KeptExisting`.
  - (d) A non-duplicate write error propagates, and no delete happens.
  - (e) In every outcome except (d), `HybridCache.RemoveAsync` runs for `"{bot}|ChatSettings|{from}"`, `"{bot}|ChatSettings|{to}"` and `"{bot}|ChatSettings|GetAllChatIds"`.

  Copy the `CreateWriteException` helper from `MongoBotControlStateStoreTests.cs`.
- [X] T017 [P] [US3] In `tests/BotFarm.Core.UnitTests/Services/MongoDbDatabaseServiceTests.cs`, add tests for `MigrateChat`, using a test subclass that overrides `MigrateBotChatData` and records calls:
  - (a) Settings are moved before `MigrateBotChatData(from, to)` is called.
  - (b) `from == to` → no database calls and no hook call.
  - (c) A hook exception propagates after the settings move completed, and the move is not undone.
  - (d) The base `MigrateBotChatData` is a no-op.
  - (e) Logs include the bot name, both chat IDs and the settings outcome.
- [X] T018 [P] [US3] In `tests/BotFarm.Core.UnitTests/Services/UpdateServiceTests.cs`, add tests:
  - (a) A `Message` update with `MigrateToChatId = S` in chat O calls `DatabaseService.MigrateChat(O, S)` once.
  - (b) A `Message` update with `MigrateFromChatId = O` in chat S calls `MigrateChat(O, S)` once.
  - (c) Neither reaches `OnUpdate`. Use the existing test subclass that records `OnUpdate`, or add one.
  - (d) An exception from `MigrateChat` is rethrown, after an Error log containing both chat IDs.
  - (e) Ordinary messages, commands and the new-member welcome are still routed as before (regression).

### Implementation for User Story 3 (Core)

- [X] T019 [P] [US3] Create `BotFarm.Core/Models/ChatSettingsMoveResult.cs`: an `internal enum ChatSettingsMoveResult { NotFound, Moved, KeptExisting }`, with XML docs. `BotFarm.Core` already exposes internals to `BotFarm.Core.UnitTests` through `InternalsVisibleTo`.
- [X] T020 [US3] Add `public async Task<ChatSettingsMoveResult> MoveChatSettings(long fromChatId, long toChatId)` to `BotFarm.Core/Services/MongoChatSettingsRepository.cs`, as in research R6 and data-model "ChatSettings":
  1. Use `_getInstance().GetCollection<BsonDocument>(nameof(ChatSettings))` and find `_id == from`.
  2. If none is found, invalidate the cache and return `NotFound`.
  3. Clone the document, set `_id = to`, and call `InsertOneAsync`. Catch only duplicate-key errors, which mean `KeptExisting`.
  4. `DeleteOneAsync(_id == from)`.
  5. Invalidate the three cache keys with the existing key formats.
  6. Return `Moved` or `KeptExisting`.
- [X] T021 [US3] Add `/// Moves all data this bot holds for fromChatId to toChatId …` `Task MigrateChat(long fromChatId, long toChatId);` to `BotFarm.Core/Abstractions/IDatabaseService.cs`, using the doc text from [contracts §2](./contracts/chat-data-contracts.md#2-database-migration-entry-point-core).
- [X] T022 [US3] In `BotFarm.Core/Base/MongoDbDatabaseService.cs`:
  - Add a public non-virtual `MigrateChat(long fromChatId, long toChatId)`. It returns early when `from == to`, calls `_chatSettings.MoveChatSettings`, logs the outcome at Information level with `Identity.LogPrefix`, bot name and both IDs, then awaits `MigrateBotChatData(from, to)` and logs its completion.
  - Add `protected virtual Task MigrateBotChatData(long fromChatId, long toChatId) => Task.CompletedTask;`. Its XML doc states the override obligations from [contracts §2](./contracts/chat-data-contracts.md#2-database-migration-entry-point-core): own database only, data under the new ID wins, safe to repeat.
  - The class needs an `ILogger` field for this; the constructor already receives `ILogger<MongoDbDatabaseService>`, so store it.
- [X] T023 [US3] Fix compile errors in any other `IDatabaseService` implementations or test doubles: run `grep -rn "IDatabaseService" --include='*.cs' BotFarm.Core BotFarm TestBot tests`. Substitutes need no change; hand-written fakes need a `MigrateChat` member.
- [X] T024 [US3] In `BotFarm.Core/Base/UpdateService.cs`:
  - In `ProcessUpdate`, add a branch before the final `else`. For `update.Type == UpdateType.Message` with `message.Type == MessageType.MigrateToChatId` and a non-null `message.MigrateToChatId`, call `OnChatMigrated(message.Chat.Id, message.MigrateToChatId.Value)`. For `MessageType.MigrateFromChatId` with a non-null `message.MigrateFromChatId`, call `OnChatMigrated(message.MigrateFromChatId.Value, message.Chat.Id)`.
  - Add `protected virtual async Task OnChatMigrated(long fromChatId, long toChatId)`. It logs the start (Information: LogPrefix, bot name, from, to) and awaits `DatabaseService.MigrateChat(from, to)`. It catches any exception, logs an Error with both IDs, and rethrows with `throw;`.
  - Update the `ProcessUpdate` XML doc list to include the migration routing.
- [X] T025 [US3] Run `dotnet test tests/BotFarm.Core.UnitTests`. All tests pass, including T016–T018. At this point every bot moves its chat settings.

### Tests for User Story 3 (TestBot GIF records) ⚠️

- [X] T026 [US3] In `tests/TestBot.UnitTests/Services/TestBotDatabaseServiceTests.cs`, add tests that call the override through `MigrateChat(from, to)` with the settings collection substituted to return nothing:
  - (a) Records found in `ChatRangeFilter(from)` produce one unordered `BulkWriteAsync` of `UpdateOneModel`s. Each has filter `_id == { ChatId: to, UserId }`, update `$setOnInsert: { FileId }` and `IsUpsert = true`. After that, `DeleteManyAsync(ChatRangeFilter(from))`.
  - (b) No records under `from` → no bulk write; the delete still runs and is harmless.
  - (c) A bulk-write exception propagates, and no delete happens.

  Depends on T002 and T008.

### Implementation for User Story 3 (TestBot GIF records)

- [X] T027 [US3] In `TestBot/Services/TestBotDatabaseService.cs`, override `protected override async Task MigrateBotChatData(long fromChatId, long toChatId)`:
  1. Read all `GifData` in `ChatRangeFilter(fromChatId)`.
  2. If there are any, run `BulkWriteAsync` with `UpdateOneModel<GifData>` items (filter `KeyFilter(toChatId, record.Id.UserId)`, update `Builders<GifData>.Update.SetOnInsert(x => x.FileId, record.FileId)`, `IsUpsert = true`) and `new BulkWriteOptions { IsOrdered = false }`.
  3. `DeleteManyAsync(ChatRangeFilter(fromChatId))`.
  4. Log the record count moved, with `Identity.LogPrefix` and both IDs.

  The XML doc states that records already under the new chat ID win, and that the method is safe to repeat (research R7). Depends on T008 and T022.
- [X] T028 [US3] Run `dotnet test tests/TestBot.UnitTests`. All tests pass, including T026.

**Checkpoint**: All user stories are independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T029 [P] Add a "Store bot data" step to the "## 🤖 Creating a New Bot" section of `README.md`, between "### 4. Register Bot Services" and "### 5. Implement the bot module", and renumber the later steps (FR-018, research R10). It covers:
  - one collection per kind of data, keyed in `_id` by chat (and user) like `GifData`;
  - why uniqueness lives in `_id` (restore recreates documents, not indexes);
  - the `_id` range pattern for chat-wide queries, linking to `TestBot/Services/TestBotDatabaseService.cs`;
  - overriding `MigrateBotChatData` when the bot stores per-chat data outside `ChatSettings`, with its three obligations.
- [X] T030 Check that the README code snippets still match the code. In the "### 4. Register Bot Services" snippet, nothing should change. Search `README.md` for `GifData`/`SaveGifData` and update any stale usage.
- [X] T031 Run the full suite: `dotnet build BotFarm.sln`, then `dotnet test tests/BotFarm.Core.UnitTests`, then `dotnet test tests/TestBot.UnitTests`. Report any failures in full, compared with the T001 baseline.
- [X] T032 Run the manual checks in [quickstart.md](./quickstart.md) §2 and §3 when a development bot token and test group are available. Report which checks ran and which were skipped.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: empty.
- **US1 (Phase 3)**: after T001.
- **US2 (Phase 4)**: after US1 (T003, T006, T008).
- **US3 Core (T016–T025)**: after T001. Independent of US1 and US2.
- **US3 TestBot (T026–T028)**: after US1 (T008) **and** US3 Core (T022).
- **Polish (Phase 6)**: after all stories. T029 needs T022 and T027 to describe the final API.

### Within Each Story

- Tests first; confirm they fail.
- US1: model (T006) → interface (T007) → service (T008) → callers (T009–T011) → test run (T012).
- US3 Core: enum (T019) → repository (T020) → interface (T021) → base service (T022) → fakes (T023) → update routing (T024) → test run (T025).

### Parallel Opportunities

- **US1 tests**: T003, T004 and T005 in parallel, each in a different file. T002 can run alongside them.
- **US1 implementation**: T009 and T010 in parallel after T007.
- **US1 and US3 Core at the same time**: T002–T012 (TestBot files) and T016–T025 (BotFarm.Core files) touch no common files.
- **US3 Core tests**: T016, T017 and T018 in parallel.
- **US2 tests**: T013 and T014 in parallel.

---

## Parallel Example: US1 and US3 Core together

```bash
# Track A (TestBot storage):
Task: "T002 Rewrite TestBotDatabaseServiceTests for the GifData collection"
Task: "T003 GifData serialization test in tests/TestBot.UnitTests/Models/GifDataSerializationTests.cs"
Task: "T004 Update TestBotUpdateServiceTests for SaveGifData(chatId, userId, fileId)"

# Track B (Core chat migration):
Task: "T016 MoveChatSettings tests in MongoChatSettingsRepositoryTests.cs"
Task: "T017 MigrateChat template tests in MongoDbDatabaseServiceTests.cs"
Task: "T018 Migration routing tests in UpdateServiceTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. T001 baseline.
2. Phase 3 (US1) → T012 green → TestBot uses the single collection.
3. **Stop and validate** with quickstart §2, steps 1–4.

### Incremental Delivery

1. US1 → MVP.
2. US2 → verification only (T013–T015).
3. US3 Core → every bot moves chat settings on migration.
4. US3 TestBot → GIF records follow the migration.
5. Polish → README and the full test run.

Commit directly to `master` after each checkpoint (US1, US2, US3 Core, US3 TestBot, Polish).

---

## Notes

- [P] tasks touch different files and don't depend on incomplete tasks.
- Story labels map tasks to spec user stories for traceability.
- Verify that the tests fail before implementing.
- Avoid same-file conflicts. `TestBotDatabaseService.cs` and `TestBotDatabaseServiceTests.cs` are edited in both US1 and US3, so run those tasks one after another.
