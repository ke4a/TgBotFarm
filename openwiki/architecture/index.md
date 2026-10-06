# Files

- [Bot Runtime Lifecycle](bot-runtime.md) - Explains how BotFarm restores and persists each bot's desired state, applies it to Telegram webhooks, gates incoming updates, and reports recoverable transition failures.
- [Persistence and Backups](persistence-and-backups.md) - Describes per-bot MongoDB databases, the separate durable bot-control store, cached chat settings, connection handling, and the local BSON ZIP backup and restore workflow.
- [System Overview](system-overview.md) - Maps the BotFarm web host, reusable libraries, and reference bot, then traces how services are composed and how the process starts and shuts down.
