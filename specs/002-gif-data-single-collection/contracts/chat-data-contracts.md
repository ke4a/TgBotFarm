# Chat Data Contracts

Internal contracts between the shared core (`BotFarm.Core`) and bot projects. No HTTP or UI surface changes.

## 1. Chat migration handling (Core → every bot)

**Trigger**: a webhook update whose `Message.Type` is `MigrateFromChatId` (posted in the new supergroup). It gives `(fromChatId = message.MigrateFromChatId, toChatId = message.Chat.Id)` as in [research.md R4](../research.md#r4-detecting-a-chat-migration).

**`UpdateService<TChatSettings>`** (BotFarm.Core/Base/UpdateService.cs)

```csharp
// ProcessUpdate logs the migration, awaits DatabaseService.MigrateChat(from, to), then calls the hook.
// On exception: logs Error with bot name, from and to, then rethrows, so UpdateControllerBase sends the
// operator alert and handles transient-retry as it does for any update. The hook is not called.

protected virtual Task OnChatMigrated(long fromChatId, long toChatId);
// Default: no-op. Runs once per handled notice, after the data move.
```

- The `MigrateFromChatId` message never reaches `OnUpdate`. The `MigrateToChatId` message posted in the old group is not used for migration and reaches `OnUpdate` like any other message.
- One-time side effects, such as a message to the upgraded chat, belong in `OnChatMigrated`. The old group no longer accepts messages, so send to `toChatId`.

## 2. Database migration entry point (Core)

**`IDatabaseService`** (BotFarm.Core/Abstractions/IDatabaseService.cs), new member:

```csharp
/// Moves all data this bot holds for fromChatId to toChatId. Data already stored under toChatId wins
/// conflicts. Safe to call repeatedly for the same pair.
Task MigrateChat(long fromChatId, long toChatId);
```

**`MongoDbDatabaseService`** (BotFarm.Core/Base/MongoDbDatabaseService.cs):

```csharp
public Task MigrateChat(long fromChatId, long toChatId);                 // non-virtual template
protected virtual Task MigrateBotChatData(long fromChatId, long toChatId); // default: no-op
```

Order inside `MigrateChat`:
1. No-op when `from == to`.
2. Move the chat settings and invalidate the cache ([data-model.md](../data-model.md#chatsettings-core-unchanged-shape)).
3. `await MigrateBotChatData(from, to)`.

A failure in step 3 propagates and does not undo step 2.

**Bot obligations**: a bot that stores per-chat data outside `ChatSettings` overrides `MigrateBotChatData`. The override must:
- touch only its own database;
- keep the data already under `toChatId` on conflict;
- be safe to repeat (completing it after a partial run must give the same end state).

## 3. TestBot database service

**`ITestBotDatabaseService`** (TestBot/Abstractions/ITestBotDatabaseService.cs):

```csharp
Task SaveGifData(long chatId, long userId, string fileId); // takes the key fields directly
Task<GifData?> GetGifData(long chatId, long userId);        // unchanged signature
Task ClearChatData(long chatId);                           // unchanged signature; now deletes the chat's range
```

`TestBotDatabaseService` overrides `MigrateBotChatData` as in [research.md R7](../research.md#r7-moving-gif-records).

Caller updates: `TestBotUpdateService` (save) and `GetLastGifCommandHandler` (reads `FileId` from the returned `GifData`).

## 4. Unchanged

- Webhook registration (`allowed_updates` stays unset), backup and restore, the dashboard and bot commands are unchanged.
- Bot-facing Telegram behavior is unchanged (FR-008), except that settings and GIFs follow a migrated chat.
