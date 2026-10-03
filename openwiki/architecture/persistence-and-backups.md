---
type: architecture
title: Persistence and Backups
description: Describes per-bot MongoDB databases, cached chat settings, connection handling, and the local BSON ZIP backup and restore workflow.
tags: [architecture, mongodb, persistence, backups]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-03T08:54:46.183Z
sources:
  - id: openwiki-source-065152a8fb68fdc8fa779e03
    resource: repo://BotFarm.Core/Abstractions/MongoDbDatabaseService.cs
  - id: openwiki-source-f1c72c08165a9648448f817e
    resource: repo://BotFarm.Core/Models/ChatSettings.cs
  - id: openwiki-source-480fc983c32a8ad500166592
    resource: repo://BotFarm.Core/Services/LocalBackupHelperService.cs
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
  - id: openwiki-source-af0728da3be4c85f0b8da66c
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoConnectionManagerTests.cs
  - id: openwiki-source-2dae7d6c8c6c0c7b1c6b4e43
    resource: repo://tests/BotFarm.Core.UnitTests/Services/MongoDbBackupServiceTests.cs
generated: { by: "copilot", at: "2026-10-03T08:54:46.183Z" }
---

# Persistence and Backups

## MongoDB ownership

`MongoDbDatabaseService` is the shared base for bot-specific persistence. It reads `ConnectionStrings:MongoDb` and defaults the database name to the bot identity in lowercase; a bot can supply another database name. The service delegates database-handle lifecycle and collection operations to `MongoConnectionManager`, and chat settings to `MongoChatSettingsRepository`.

The Mongo client factory creates a `MongoClient` for the supplied connection string. A database handle from `GetDatabase` does not itself prove connectivity: `MongoConnectionManager.Reconnect` explicitly pings MongoDB. A failed reconnect logs and notifies, returns failure, and requests host shutdown. `Disconnect` clears the database handle; it does not dispose the shared client. Collection-list and collection-read failures are logged and return empty sequences, while unavailable database statistics return `null` with a warning.

Bot data is separate from dashboard identity storage: the web host configures MongoDB-backed ASP.NET Identity with database name `BotFarmIdentity`. TestBot's database uses its bot identity as the default database name. Its GIF data is stored in one collection per chat, with the user ID identifying the saved GIF within that collection. See [System Overview](./system-overview.md) for service composition and [Operator Dashboard](../workflows/operator-dashboard.md) for how operators use database-backed features.

## Chat settings and cache

`MongoChatSettingsRepository` stores `ChatSettings` documents in the `ChatSettings` collection, keyed by `ChatId`, and uses `HybridCache` for individual settings and the list of known chat IDs. Cache keys include the bot name so the same chat ID in two bots does not share a cache entry. Reads populate the cache; saves and updates use MongoDB upserts and refresh relevant cached values.

When a chat has no language or its language is blank, `GetChatLanguage` writes the configured default language and returns it. Updating settings for a newly discovered chat invalidates the cached chat-ID list; updating a chat already in that list leaves it intact. Focused tests cover default-language persistence, cached reads, and cache invalidation.

## Backup creation and retention

`MongoDbBackupService` resolves a bot's database through `IBotRegistry`, reads each collection as BSON documents, serializes those documents into temporary `.bson` files, and adds one file per collection to a ZIP archive. Archives are stored locally under `backups/<bot-name>/` with timestamped names. After a successful archive creation, the service asks `LocalBackupHelperService` to keep the newest seven archives by default; older files are deleted by creation time.

The local helper also lists archive metadata, resolves a named archive path, and removes archives. Missing backup folders produce an empty list for browsing. File-system failures are logged and sent to the notification service; operations that return `Result` report failure rather than success.

## Restore lifecycle and limits

Restore first resolves the bot and database services and asks the bot to pause webhook delivery. If pausing fails, restore returns failure without processing the archive. Once paused, each ZIP entry is extracted to a temporary file and read as BSON documents. For a non-empty entry, the service drops the existing collection and recreates/populates it only if the drop succeeds. Empty entries are skipped, so they do not clear an existing collection.

The restore is collection-by-collection, not an all-or-nothing transaction. An exception is logged and notified; the bot is resumed in `finally` after a successful pause, even when processing fails. Consequently, a failure partway through can leave earlier collections restored and later ones unchanged. A failure to resume is handled by `BotService.Resume`, which requests application shutdown.

The core tests cover failed reconnect shutdown, default-language persistence, backup creation failures, restore refusal when pause fails, skipped empty collections, skipped population after a failed drop, and resume after a restore exception. The operator-facing backup actions are described in [Operator Dashboard](../workflows/operator-dashboard.md); scheduled backups and shutdown behavior are in [Configuration and Operations](../operations/deployment.md).
