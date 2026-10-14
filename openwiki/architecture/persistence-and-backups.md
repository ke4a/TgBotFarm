---
type: architecture
title: Persistence and Backups
description: Describes per-bot MongoDB databases, the separate durable bot-control store, cached chat settings, connection handling, and the local BSON ZIP backup and restore workflow.
tags: [architecture, mongodb, persistence, backups]
sources:
  - id: openwiki-source-7ad51b6d1b593870aa816553
    resource: repo://BotFarm.Core/Abstractions/IDatabaseService.cs
  - id: openwiki-source-065152a8fb68fdc8fa779e03
    resource: repo://BotFarm.Core/Abstractions/MongoDbDatabaseService.cs
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
  - id: openwiki-source-5a967c2a70e92f59eae7aebf
    resource: repo://BotFarm/Startup.cs
  - id: openwiki-source-4cf4590d989180df848e74b8
    resource: repo://TestBot/Services/TestBotDatabaseService.cs
  - id: openwiki-source-886ac5a81ab72a61e12caed1
    resource: repo://tests/BotFarm.Core.UnitTests/Services/LocalBackupHelperServiceTests.cs
  - id: openwiki-source-3a8c43e08d5c8e303fca443f
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoBotControlStateStoreTests.cs
  - id: openwiki-source-af0728da3be4c85f0b8da66c
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoConnectionManagerTests.cs
  - id: openwiki-source-2dae7d6c8c6c0c7b1c6b4e43
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoDbBackupServiceTests.cs
generated: { by: "copilot", at: "2026-10-14T09:31:37.432Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-10-14T09:31:37.432Z
---

# Persistence and Backups

## MongoDB ownership

`AddCoreServices` registers one shared `IMongoClient` from `ConnectionStrings:MongoDb` and throws when the setting is missing. `MongoDbDatabaseService` receives that client and defaults its database name to the lowercase bot identity; a bot can supply another database name. It delegates database-handle lifecycle and collection operations to `MongoConnectionManager`, and chat settings to `MongoChatSettingsRepository`.

`GetDatabase` alone does not prove connectivity: `MongoConnectionManager.Reconnect` explicitly pings MongoDB. A failed reconnect logs and notifies, returns failure, and requests host shutdown. `Disconnect` clears the database handle without disposing the shared client. An unavailable database returns `null` statistics with a warning. Collection listing and BSON reads propagate database exceptions; create, drop, populate, and rename operations log and notify failures and return `false`.

`IDatabaseService` is the common bot-persistence contract for connection lifecycle, chat IDs/counts, and language settings. `IMongoDbDatabaseService` adds collection and database-stat operations used by backup and inspection features. Chat and collection counts default to MongoDB's estimated count; callers can request an exact count. The web host configures MongoDB-backed ASP.NET Identity separately with database name `BotFarmIdentity`. TestBot's database uses its bot identity as the default database name. Its GIF data is stored in one collection per chat, with the user ID identifying the saved GIF within that collection. See [System Overview](./system-overview.md) for service composition and [Operator Dashboard](../workflows/operator-dashboard.md) for how operators use database-backed features.

## Durable bot-control state

Bot lifecycle desired state has its own persistence boundary: `MongoBotControlStateStore` uses the `BotFarmControl` database and the `BotControlStates` collection, with one record per bot name. Records contain the desired enabled flag, the last command ID, and update time; process-local applied status is deliberately not part of this document. Desired-state updates use MongoDB find-and-update and return the updated record. The store creates its database handle lazily, so a failed initial factory call can be retried by a later operation.

`BotControlState` ignores additional BSON fields. This preserves deserialization compatibility with older state documents containing the removed `Revision` field, as covered by `MongoBotControlStateStoreTests`.

Per-bot backup archives do not include this control database. `MongoDbBackupService` resolves the selected bot's `IMongoDbDatabaseService` through `IBotRegistry` and archives that bot database's collections; restore uses the same bot-scoped database service. Plan separately for control-state preservation if lifecycle intent must survive replacement or recovery of MongoDB. See [Bot Runtime Lifecycle](./bot-runtime.md) for how desired state is applied and update processing is gated.

## Chat settings and cache

`MongoChatSettingsRepository` stores `ChatSettings` documents in the `ChatSettings` collection, keyed by `ChatId`, and uses `HybridCache` for individual settings and the list of known chat IDs. Cache keys include the bot name so the same chat ID in two bots does not share a cache entry. Reads populate the cache; saves and updates use MongoDB upserts and refresh relevant cached values. Enumerating all chat settings also seeds each yielded setting in the per-chat cache.

When a chat has no language or its language is blank, `GetChatLanguage` writes the configured default language and returns it. Updating settings for a newly discovered chat invalidates the cached chat-ID list; updating a chat already in that list leaves it intact. Focused tests cover default-language persistence, cached reads, and cache invalidation.

## Backup creation and retention

`MongoDbBackupService` resolves a bot's database through `IBotRegistry`, pauses its update delivery, and writes one `<collection>.bson` entry per collection into a ZIP archive. Each BSON document is serialized to a seekable in-memory buffer before it is appended to the ZIP stream. The bot is resumed in `finally`; a failed archive operation removes the partial archive and returns failure. Archives are stored locally under `backups/<bot-name>/` with timestamped names. After a successful archive creation, the service asks `LocalBackupHelperService` to keep the newest seven archives by default; older files are deleted by creation time.

The local helper also lists archive metadata, resolves a named archive path, and removes archives. Missing backup folders produce an empty list for browsing. File-system failures are logged and sent to the notification service; operations that return `Result` report failure rather than success.

## Restore lifecycle and limits

Restore first resolves the bot and database services and asks the bot to pause webhook delivery. If pausing fails, restore returns failure without processing the archive. Once paused, each ZIP entry is extracted to a temporary file and read in batches of up to 500 BSON documents. The service builds each entry in a uniquely named `_restore_...` staging collection, then renames that collection over the destination with `DropTarget` enabled. Empty entries create an empty staging collection and replace the destination too. If staging or rename fails, the destination is not dropped separately; the temporary staging collection and extracted file are cleaned up.

The restore remains collection-by-collection, not an archive-wide transaction. An exception is logged and notified; the bot is resumed in `finally` after a successful pause, even when processing fails. Consequently, a failure partway through can leave earlier collections restored and later ones unchanged. A failure to resume is handled by `BotService.Resume`, which requests application shutdown.

The core tests cover failed reconnect shutdown, count selection, default-language persistence, backup creation failures, and restore refusal when pause fails. Restore tests verify 500-document batching, empty-collection replacement, cleanup and preservation of the target when staging or rename fails, and resume after a restore exception. The operator-facing backup actions are described in [Operator Dashboard](../workflows/operator-dashboard.md); scheduled backups and shutdown behavior are in [Configuration and Operations](../operations/deployment.md).
