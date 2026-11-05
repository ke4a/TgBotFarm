# Implementation Plan: Store TestBot GIF Data in One Collection, and Follow Chat ID Changes in All Bots

**Branch**: `master` (commits go directly to master) | **Date**: 2026-11-05 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-gif-data-single-collection/spec.md`

## Summary

TestBot replaces its per-chat collections with one `GifData` collection. Its `_id` is a compound `{ ChatId, UserId }` key, so the uniqueness rule is enforced by the `_id` index, which survives restore. Chat-wide operations use an `_id` range, so no secondary index is needed. Existing data is not migrated.

For chat ID changes, `UpdateService<TChatSettings>` recognizes both Telegram migration service messages and calls a new `IDatabaseService.MigrateChat(from, to)`. In `MongoDbDatabaseService` that is a template method: it moves `ChatSettings` (insert-then-delete, data under the new ID wins, cache invalidated) and then calls an overridable `MigrateBotChatData` hook. TestBot overrides the hook to move its GIF records. Failures go through the existing update-controller alert and retry pipeline.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**: Existing MongoDB.Driver 3.12, Telegram.Bot 22.10, Microsoft.Extensions.Caching.Hybrid. No new packages.

**Storage**: MongoDB, one database per bot. New `GifData` collection in TestBot's database. Shape of Core's `ChatSettings` collection is unchanged.

**Testing**: NUnit with NSubstitute in `tests/BotFarm.Core.UnitTests` and `tests/TestBot.UnitTests`. MongoDB and Telegram are substituted.

**Target Platform**: Linux container (production); .NET development environments

**Project Type**: Multi-project ASP.NET Core application: host, shared core, shared UI, bot modules

**Performance Goals**: GIF lookup is one `_id` index probe whatever the collection size. Clearing or migrating a chat touches only that chat's index range.

**Constraints**:
- Backups store documents only, so uniqueness must not depend on secondary indexes.
- MongoDB can't update `_id` in place.
- Telegram sends each migration notice once per message, possibly as two messages that may race.
- A migration must be safe to repeat.

**Scale/Scope**: TestBot is a sample bot with small data. The migration handling applies to every registered bot (currently TestBot only).

No NEEDS CLARIFICATION items remain. One open product decision is recorded in [research.md R6](./research.md#r6-moving-chat-settings-safely): the settings conflict rule. The plan follows the spec ("new wins").

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Check | Pre-design | Post-design |
|---|---|---|---|
| I. Layered ownership | Migration detection and the settings move live in Core (`UpdateService`, `MongoDbDatabaseService`, `MongoChatSettingsRepository`). GIF logic stays in TestBot through an overridable hook. No new project references or DI registrations. | ✅ | ✅ |
| II. Secure configuration | No configuration or credential changes. Logs contain bot name and chat IDs only. | ✅ | ✅ |
| III. Behavior-focused tests | New and updated tests in the existing projects, with substitutes for MongoDB and Telegram ([quickstart.md §1](./quickstart.md#1-automated-tests-routine-no-live-services)). | ✅ | ✅ |
| IV. Isolated persistent data | Every move runs inside one bot's own database service. Cache invalidation uses that bot's keys. Restore behavior is unchanged and uniqueness survives it (R1). | ✅ | ✅ |
| V. Operable services | Migration logs give each owner's outcome. Failures use the existing alert and transient-retry path (R8). No change to backups or health checks. | ✅ | ✅ |
| Workflow: data changes need a migration or rollback plan | Existing TestBot data is not migrated (spec Assumptions). Rollback means redeploying the previous build: it reads only legacy collections, so GIFs stored after the upgrade become invisible to it but aren't deleted. Moved settings stay under the new chat ID, which is correct for the upgraded group. | ✅ | ✅ |

Result: **PASS**, no violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/002-gif-data-single-collection/
├── plan.md                         # This file
├── research.md                     # Phase 0 decisions R1–R10
├── data-model.md                   # GifData, ChatSettings move, migration states
├── quickstart.md                   # Validation guide
├── contracts/
│   └── chat-data-contracts.md      # Core ↔ bot contracts
├── checklists/
│   └── requirements.md             # Spec quality checklist
└── tasks.md                        # Created by /speckit-tasks
```

### Source Code (repository root)

```text
BotFarm.Core/
├── Abstractions/IDatabaseService.cs          # + MigrateChat(from, to)
├── Base/MongoDbDatabaseService.cs            # + MigrateChat template, + protected virtual MigrateBotChatData
├── Base/UpdateService.cs                     # + migration-message branch, + protected virtual OnChatMigrated
├── Models/ChatSettingsMoveResult.cs          # new internal enum (NotFound, Moved, KeptExisting)
└── Services/MongoChatSettingsRepository.cs   # + MoveChatSettings(from, to) with cache invalidation

TestBot/
├── Models/GifData.cs                         # _id becomes GifDataKey; + GifDataKey { ChatId, UserId }
├── Abstractions/ITestBotDatabaseService.cs   # SaveGifData(chatId, userId, fileId)
├── Services/TestBotDatabaseService.cs        # single GifData collection, _id range helper, MigrateBotChatData override
├── Services/TestBotUpdateService.cs          # caller of new SaveGifData signature
└── Handlers/Commands/GetLastGifCommandHandler.cs  # adapts to model change if needed

tests/BotFarm.Core.UnitTests/Services/
├── UpdateServiceTests.cs                     # migration routing
├── MongoChatSettingsRepositoryTests.cs       # settings move
└── MongoDbDatabaseServiceTests.cs            # template order and failure

tests/TestBot.UnitTests/
├── Services/TestBotDatabaseServiceTests.cs   # rewritten for the single collection + migration
├── Services/TestBotUpdateServiceTests.cs     # save signature
└── Handlers/Commands/GetLastGifCommandHandlerTests.cs

README.md                                     # "Creating a New Bot": add "Store bot data" step (R10)
```

**Structure Decision**: Use the existing projects. Core changes stay in `BotFarm.Core` (base classes, abstraction, settings repository). Bot-specific changes stay in `TestBot`. Tests extend the matching existing test classes.

## Implementation Order

1. **Core: settings move.** `ChatSettingsMoveResult`, `MongoChatSettingsRepository.MoveChatSettings`, and its tests.
2. **Core: template.** `IDatabaseService.MigrateChat`, `MongoDbDatabaseService.MigrateChat` and `MigrateBotChatData`, and their tests.
3. **Core: detection.** `UpdateService` migration branch and `OnChatMigrated`, and their tests. After this step, Story 3 works for every bot's chat settings.
4. **TestBot: storage.** `GifData`/`GifDataKey`, the rewritten `TestBotDatabaseService` (get, save with retry, clear by range), caller updates, and tests (Stories 1–2).
5. **TestBot: migration hook.** The `MigrateBotChatData` override and its tests (Story 3 for GIFs).
6. **Docs.** README step (FR-018).
7. **Validate.** Run both test projects, then the manual checks in [quickstart.md](./quickstart.md).

Steps 1–3 and step 4 are independent. Step 5 depends on steps 2 and 4.

## Risks

| Risk | Mitigation |
|---|---|
| The `_id` range relies on `ChatId` serializing before `UserId`. | Declare properties in that order and pin the order with `[BsonElement(Order = …)]`. A unit test asserts the rendered filter and the serialized key layout. |
| "New wins" for settings can discard a chosen language if defaults were created first (R6). | Flagged as an open product decision. Switching to old-wins is a local change in `MoveChatSettings`. |
| A migration notice arrives while a bot is disabled. | The controller returns 503 while a bot is disabled, so Telegram redelivers later. A permanently missed notice is out of scope (spec Assumptions). |
