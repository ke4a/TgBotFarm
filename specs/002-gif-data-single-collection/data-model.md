# Data Model: Store TestBot GIF Data in One Collection, and Follow Chat ID Changes in All Bots

**Feature**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

All collections live in the owning bot's own MongoDB database (Constitution IV).

## GifData (TestBot, new layout)

**Collection**: `GifData` (one per TestBot database; replaces the per-chat collections named `{chatId}`)

| Field | BSON | Type | Rules |
|---|---|---|---|
| `Id` | `_id` | `GifDataKey` | Required. Unique, enforced by the `_id` index, which survives restore (R1). |
| `Id.ChatId` | `_id.ChatId` | `long` | Telegram chat ID; negative for groups. Always serialized **before** `UserId` (R2 depends on this order). |
| `Id.UserId` | `_id.UserId` | `long` | Telegram user ID of the sender. |
| `FileId` | `FileId` | `string` | Required, non-empty Telegram file identifier of the last GIF. |

**Example document**

```json
{ "_id": { "ChatId": -1001234567890, "UserId": 42 }, "FileId": "CgACAgIAAxkBAAI..." }
```

**Operations**

| Operation | Filter | Write | Notes |
|---|---|---|---|
| Get last GIF | `_id == { ChatId: c, UserId: u }` | — | Single `_id` index probe (FR-006). |
| Save GIF | `_id == { ChatId: c, UserId: u }` | Replace, upsert | Retry once on a duplicate key (R3, FR-005). |
| Clear chat | `_id` in `[{c, MinKey}, {c, MaxKey}]` | `DeleteMany` | Uses the `_id` index (R2, FR-007). |
| Migrate chat | read the range for `from`; upsert each record as `{to, UserId}` with `$setOnInsert`; then delete the range for `from` | bulk upsert + `DeleteMany` | The record already under `to` wins. Safe to repeat (R7). |

**Legacy layout (ignored)**: Collections whose name is a chat ID, holding `{ _id: UserId, FileId }`, are neither read, converted nor removed (FR-009).

## ChatSettings (Core, unchanged shape)

**Collection**: `ChatSettings`. `_id` = `ChatId` (`long`), plus `Language` and fields from derived types such as `TestBotChatSettings`.

**New operation: move to a new chat ID** (R6)

| Step | Action | Outcome |
|---|---|---|
| 1 | Read `_id == from` as `BsonDocument` | none → `NotFound` |
| 2 | Insert a copy with `_id = to` | duplicate key → `KeptExisting` |
| 3 | `DeleteOne(_id == from)` | `Moved` or `KeptExisting` |
| 4 | Remove cache keys `{bot}\|ChatSettings\|{from}`, `{bot}\|ChatSettings\|{to}`, `{bot}\|ChatSettings\|GetAllChatIds` | — |

`ChatSettingsMoveResult` (Core, internal enum): `NotFound`, `Moved`, `KeptExisting`. It's used only for logging (FR-017).

## Chat migration (transient, not persisted)

A pair `(FromChatId, ToChatId)` taken from a Telegram service message (R4). Validation: both non-zero; when `From == To` the migration is a no-op. It is never stored. Repeat safety comes from the move operations themselves, not from a record of migrations already handled.

## State transitions for one chat's data during a migration

```text
[only under From] --insert copy--> [under both] --delete From--> [only under To]
[under both, To existed before]   --delete From-->                [only under To (To's data kept)]
[only under To] (already done)    --no-op-->                      [only under To]
```

A crash at any point leaves one of these states. The next handling, such as a redelivered notice, always ends in `[only under To]`.
