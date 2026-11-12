---
type: architecture
title: Persistence and Backups
description: Describes BotFarm's MongoDB ownership, the durable bot-control database, cached chat settings, and how per-bot backup and restore archives are created, paused around, stored in the data folder, and pruned.
tags: [architecture, mongodb, backups, persistence]
sources:
  - id: openwiki-source-7ad51b6d1b593870aa816553
    resource: repo://BotFarm.Core/Abstractions/IDatabaseService.cs
  - id: openwiki-source-c20f310ef0f21eb05cbaa2d6
    resource: repo://BotFarm.Core/Base/MongoDbDatabaseService.cs
  - id: openwiki-source-01ac72d9bed3a42f79f9d578
    resource: repo://BotFarm.Core/Extensions/MongoCollectionCountExtensions.cs
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-7e0fbf7825e41e66fdf4eae2
    resource: repo://BotFarm.Core/Models/BotControlState.cs
  - id: openwiki-source-7d566a7bdfb7e136e4ec44fd
    resource: repo://BotFarm.Core/Models/BotRuntimeStatus.cs
  - id: openwiki-source-f1c72c08165a9648448f817e
    resource: repo://BotFarm.Core/Models/ChatSettings.cs
  - id: openwiki-source-8ca2e1285d7e7a015b568dd0
    resource: repo://BotFarm.Core/Models/ChatSettingsMoveResult.cs
  - id: openwiki-source-480fc983c32a8ad500166592
    resource: repo://BotFarm.Core/Services/LocalBackupHelperService.cs
  - id: openwiki-source-a06f6c23ee056cc162bc8d5a
    resource: repo://BotFarm.Core/Services/MongoBotControlStateStore.cs
  - id: openwiki-source-c36511a0bb6e7ceb140fb521
    resource: repo://BotFarm.Core/Services/MongoChatSettingsRepository.cs
  - id: openwiki-source-1fb310329694d31da9ee3a1e
    resource: repo://BotFarm.Core/Services/MongoConnectionManager.cs
  - id: openwiki-source-fda3ca8d1dc7d40f51b3c1ea
    resource: repo://BotFarm.Core/Services/MongoDbBackupService.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-7f1ffe359debc4d1ba6e1788
    resource: repo://TestBot/Models/GifData.cs
  - id: openwiki-source-4cf4590d989180df848e74b8
    resource: repo://TestBot/Services/TestBotDatabaseService.cs
  - id: openwiki-source-886ac5a81ab72a61e12caed1
    resource: repo://tests/BotFarm.Core.UnitTests/Services/LocalBackupHelperServiceTests.cs
  - id: openwiki-source-3a8c43e08d5c8e303fca443f
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoBotControlStateStoreTests.cs
  - id: openwiki-source-8e270948a1f32961a4a6e0c7
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoChatSettingsRepositoryTests.cs
  - id: openwiki-source-af0728da3be4c85f0b8da66c
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoConnectionManagerTests.cs
  - id: openwiki-source-2dae7d6c8c6c0c7b1c6b4e43
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoDbBackupServiceTests.cs
  - id: openwiki-source-994de5f20c7946d212aa2b30
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoDbDatabaseServiceTests.cs
  - id: openwiki-source-b483c50f88a0afca9d95a38a
    resource: repo://tests/TestBot.UnitTests/Models/GifDataSerializationTests.cs
  - id: openwiki-source-1aa36b0f627137c2408dc39b
    resource: repo://tests/TestBot.UnitTests/Services/TestBotDatabaseServiceTests.cs
generated: { by: "claude-code", at: "2026-11-12T18:30:04.833Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T18:30:04.833Z
---

# Persistence and Backups

## MongoDB ownership

`AddCoreServices` registers one shared `IMongoClient` from `ConnectionStrings:MongoDb`; resolving it throws when the setting is missing. Each bot's database service derives from `MongoDbDatabaseService`, which receives that client, keyed `BotIdentity`, notifications, and `HybridCache`. Its database name defaults to the lowercase bot name, and a bot can supply another. It delegates the database handle and collection operations to `MongoConnectionManager`, and chat settings to `MongoChatSettingsRepository`.

`MongoConnectionManager` gets its database handle once, in its constructor; there is no reconnect or disconnect step, and the shared client is never disposed by a bot. Database statistics failures are logged and return `null` without an alert. Collection listing and BSON reads let exceptions propagate to the caller. Create, drop, populate, rename, and index-creation failures are logged, sent to the admin chat through the notification service, and return `false`. `GetIndexDefinitions` lists a collection's indexes without the automatic `_id_` index and strips the `v` and `ns` fields so the definitions can be recreated elsewhere; a collection that does not exist (MongoDB error code 26) yields an empty list, while other listing errors propagate. `CreateIndexes` recreates such definitions with one `createIndexes` command.

`IDatabaseService` is the common bot-persistence contract for chat IDs/counts and language settings. `IMongoDbDatabaseService` adds collection, index-definition, and database-stat operations used by backups and the dashboard. Chat and collection counts default to MongoDB's estimated count; callers can request an exact count.

The web host configures MongoDB-backed ASP.NET Identity separately, in the `BotFarmIdentity` database. See [System Overview](./system-overview.md) for service composition.

## Bot data layout: TestBot GIFs

TestBot keeps every GIF record in a single `GifData` collection. The document `_id` is a compound `GifDataKey` of `ChatId` and `UserId`, serialized in that order, so each chat and user has at most one record. Uniqueness lives in `_id`, which is always unique and indexed, so it holds in every collection the bot or a restore creates without needing a secondary unique index.

`SaveGifData` replaces the record for a chat and user with an upsert. If a concurrent save makes the upsert's insert fail with a duplicate key, the replace is retried once; the last write wins. Chat-wide reads and deletes, such as `ClearChatData`, use an `_id` range from `{ChatId, UserId: MinKey}` to `{ChatId, UserId: MaxKey}`. That range covers exactly one chat only because `ChatId` is serialized first, and it is served by the `_id` index.

## Chat migration

When Telegram upgrades a group to a supergroup, the update router calls `IDatabaseService.MigrateChat(fromChatId, toChatId)` (see [Bot Runtime Lifecycle](./bot-runtime.md)). The contract is that data already stored under the new chat ID wins conflicts and that the call is safe to repeat for the same pair, since Telegram announces the upgrade in both chats.

`MongoDbDatabaseService.MigrateChat` does nothing when both IDs are equal. Otherwise it moves the chat settings first, then calls the virtual `MigrateBotChatData` for the bot's own per-chat data; the default does nothing. A failure in `MigrateBotChatData` propagates and leaves the moved settings in place. An override must touch only the bot's database, keep data under the target chat on conflict, and reach the same end state when completing a partial run.

`MongoChatSettingsRepository.MoveChatSettings` handles the settings document as raw BSON, so fields of derived settings types survive. It inserts a copy under the target ID before deleting the source, so an interrupted move finishes on the next call. A duplicate-key insert means the target already had settings, which are kept; the source is removed either way. The result is `NotFound`, `Moved`, or `KeptExisting`, and the cache entries for both chats and the chat-ID list are invalidated in every case.

TestBot's `MigrateBotChatData` reads the source chat's GIF records, upserts each under the target chat with `$setOnInsert` in an unordered bulk write, so a record already stored for the same user under the target wins, and then deletes the source records. A rerun repeats only no-op upserts and the delete.

## Chat deletion

`IDatabaseService.DeleteChat(chatId)` removes everything a bot holds for a chat, including its settings, so the chat is no longer listed; it is safe to repeat. The dashboard calls it to delete one chat or to clean up chats the bot can no longer reach (see [Operator Dashboard and Administration](../workflows/operator-dashboard.md)).

`MongoDbDatabaseService.DeleteChat` first calls the virtual `DeleteBotChatData` for the bot's own per-chat data (the default does nothing), then `MongoChatSettingsRepository.DeleteChatSettings`, which deletes the settings document by `_id`, removes the chat's cache entry and the cached chat-ID list, and reports whether a document existed. If `DeleteBotChatData` fails, the exception propagates and the settings are kept, so the chat stays listed and the deletion can be retried. An override must touch only the bot's database and be safe to repeat, including for a chat with no data. TestBot's override deletes the chat's GIF records with `ClearChatData`.

## Durable bot-control state

Bot desired state has its own persistence boundary: `MongoBotControlStateStore` uses the `BotFarmControl` database and its `BotControlStates` collection, one record per bot name. Records hold the desired enabled flag, the last command ID, and the update time; process-local applied status is not stored. Desired-state updates use find-and-update and return the updated record. The store creates its database handle lazily, so a failed first attempt can be retried by a later operation.

`BotControlState` ignores extra BSON fields, so older documents that still contain a removed `Revision` field deserialize, as covered by `MongoBotControlStateStoreTests`.

Per-bot backup archives do not include this control database: backup and restore resolve only the selected bot's `IMongoDbDatabaseService` through `IBotRegistry`. Preserve `BotFarmControl` separately if lifecycle intent must survive MongoDB replacement. See [Bot Runtime Lifecycle](./bot-runtime.md) for how desired state is applied.

## Chat settings and cache

`MongoChatSettingsRepository` stores `ChatSettings` documents in the `ChatSettings` collection keyed by `ChatId`, and uses `HybridCache` for individual settings and for the list of known chat IDs. Cache keys include the bot name, so the same chat ID in two bots never shares an entry. Reads populate the cache; saves and updates use upserts and refresh the cached values; deletes remove the cached entry. Enumerating all chat settings also seeds each yielded entry in the per-chat cache. Every cache entry is tagged with the bot name, which lets a restore drop all of a bot's cached settings at once.

When a chat has no language or a blank one, `GetChatLanguage` writes the default language and returns it. Updating settings always drops the cached chat-ID list instead of loading it to check whether the chat is new, so the list is reloaded only when next needed. The chat count uses MongoDB's estimated count and never loads the ID list.

## Backup creation and retention

`MongoDbBackupService.BackupDatabase` first checks that the bot is registered, returning a failure otherwise. It then runs the archive work inside `IBotControlService.RunWithUpdatesPausedAsync`, so an enabled bot's webhook is paused for the duration and its desired state is re-applied afterwards. If the pause cannot be confirmed, no archive is created; the service alerts and returns failure.

Inside the pause, it writes one `<collection>.bson` ZIP entry per collection, followed by an `indexes/<collection>.bson` entry holding that collection's index definitions. The index entry is written even when the collection has no secondary indexes, so a restore can tell an empty list from an archive that predates index saving. Each document is serialized to a seekable in-memory buffer before being appended, because BSON serialization backpatches the document length. A failure is logged and alerted, the partial archive is deleted, and the call returns failure. Cancellation also deletes the partial archive but rethrows without an alert. After a successful archive, the service asks `LocalBackupHelperService` to keep the seven newest archives by creation time and returns the archive file name as result metadata.

Archives live under the data folder's `backups/<bot-name>/` directory (see `IAppDataPaths`) with `yyyyMMddHHmmss.zip` names. The local helper also lists archive metadata newest-first (only `.zip` files), resolves a named archive path, and deletes archives. A missing folder yields an empty list. File-system failures are logged and alerted; operations that return `Result` report failure.

## Restore lifecycle and limits

`RestoreBackup` resolves the bot's database service and archive path, then runs inside `RunWithUpdatesPausedAsync`. If the pause fails, restore returns failure without reading the archive. Each ZIP entry is extracted to the data folder's temporary directory and read in batches of up to 500 BSON documents into a uniquely named `_restore_<guid>` staging collection. The staging collection is then renamed over the destination with `DropTarget` enabled; an empty entry creates an empty staging collection and still replaces the destination. Entries under `indexes/` are not restored as collections; they are read together with their collection. Before the rename, the archived index definitions are created on the staging collection; an archive without an index entry for the collection (made before indexes were saved) reuses the destination's current indexes instead. If staging, index creation, or rename fails, the destination is left untouched, and the staging collection and extracted file are cleaned up.

Restore replaces collections one by one and is not an archive-wide transaction: a failure partway through can leave earlier collections restored and later ones unchanged. Any exception is logged and alerted, and the call returns failure. Whether the restore succeeded or not, the service then removes every `HybridCache` entry tagged with the bot name, so cached chat settings never outlive data they no longer match, even after a partial restore. Because the coordinator owns the pause, the bot's desired state is re-applied after the restore whether it succeeded or not; if re-enabling fails, the bot goes to `Error` and admins are alerted.

## Focused tests

`MongoChatSettingsRepositoryTests` cover the settings cache (including updates that drop the chat-ID list without loading it and an estimated chat count), settings moves (a missing source, copy-then-delete, a kept existing target, and keeping the source when the insert fails for another reason), and settings deletion with cache removal. `MongoDbDatabaseServiceTests` cover `MigrateChat` ordering, equal IDs, failure propagation with settings left moved, the no-op default hook, and logging, plus `DeleteChat` ordering (bot data before settings) and keeping settings when the bot hook fails. `TestBotDatabaseServiceTests` cover compound-key lookups, the upsert and its single duplicate-key retry, chat-range deletes that never drop a collection, GIF migration (existing target records kept, no records, bulk-write failure leaving the source), and deleting a chat's GIF records. `GifDataSerializationTests` cover the `ChatId`-first key order and reading restored documents. `MongoConnectionManagerTests` cover the single database handle, stats mapping and failure, exception propagation for listing and reads, and false-plus-alert results for create/drop/populate/rename, and index-definition reads and creation. `MongoDbBackupServiceTests` cover unknown bots, refusal when pausing fails, cancellation and partial-archive cleanup, writing each collection's index definitions, creating archived indexes before the rename and keeping current indexes for older archives, 500-document batching, empty-collection replacement, target preservation when staging, index creation, or rename fails, clearing the bot's cache after a restore, and alerting after restore exceptions. `LocalBackupHelperServiceTests` cover retention, listing order, and missing files. Operator actions are described in [Operator Dashboard](../workflows/operator-dashboard.md); scheduled backups are in [Configuration and Operations](../operations/deployment.md).
