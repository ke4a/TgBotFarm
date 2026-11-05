# Quickstart: Validating the GIF Store and Chat Migration

**Feature**: [spec.md](./spec.md) | **Contracts**: [contracts/chat-data-contracts.md](./contracts/chat-data-contracts.md) | **Data model**: [data-model.md](./data-model.md)

## Prerequisites

- .NET 10 SDK.
- For the manual checks only: a development TestBot token, a reachable webhook URL (see README "WebHook URL Options"), a local MongoDB and a Telegram test group.

## 1. Automated tests (routine; no live services)

```bash
dotnet test tests/BotFarm.Core.UnitTests
```

```bash
dotnet test tests/TestBot.UnitTests
```

Expected: all tests pass. The new and changed tests cover:

| Area | Test class | Scenarios |
|---|---|---|
| Migration routing | `UpdateServiceTests` | `MigrateToChatId` → `MigrateChat(chat, migrateTo)`; `MigrateFromChatId` → `MigrateChat(migrateFrom, chat)`; neither reaches `OnUpdate`; an exception is rethrown. |
| Settings move | `MongoChatSettingsRepositoryTests` | NotFound; Moved; KeptExisting on duplicate key; both chat keys and the chat-ID list removed from the cache. |
| Template order | `MongoDbDatabaseServiceTests` | settings before bot hook; `from == to` is a no-op; a hook failure propagates after settings moved. |
| GIF store | `TestBotDatabaseServiceTests` | `_id` filter for get/save; single retry on duplicate key; clear uses the `_id` range; migrate upserts with `$setOnInsert` and then deletes the old range. |
| Callers | `TestBotUpdateServiceTests`, `GetLastGifCommandHandlerTests` | new `SaveGifData` signature; `FileId` read from the key-based model. |

## 2. Manual check: single collection (Stories 1–2)

1. Start the app with TestBot enabled against an empty database.
2. In two chats, send GIFs from two users. Use the last-GIF command in each and confirm the right GIF comes back.
3. In the dashboard database stats or `mongosh`, confirm TestBot's database has a `GifData` collection and no collection named after a chat ID.
4. Clear one chat's data and confirm the other chat's GIFs still come back.
5. Run a backup, send a newer GIF, restore the backup, and confirm the older GIF comes back.

## 3. Manual check: chat migration (Story 3)

1. Add TestBot (and any other registered bot) to a **basic** Telegram group. Set a non-default language and store a GIF.
2. Upgrade the group to a supergroup, for example by making its history visible to new members or giving it a public link.
3. Expected logs per bot: one Information entry per migration notice with the bot name and both chat IDs. The first notice reports the settings moved; the second reports nothing left to move.
4. In the supergroup, confirm the language stayed and the last-GIF command returns the stored GIF.
5. In the database, confirm no `ChatSettings` document or `GifData` record remains under the old chat ID.
