# Files

- [Bot Runtime Lifecycle](bot-runtime.md) - Explains how BotFarm restores and persists each bot's desired state, applies it to Telegram webhooks, pauses bots for maintenance and shutdown, gates incoming updates, and routes updates to handlers.
- [Persistence and Backups](persistence-and-backups.md) - Describes BotFarm's MongoDB ownership, the durable bot-control database, cached chat settings, and how per-bot backup and restore archives are created, paused around, stored in the data folder, and pruned.
- [System Overview](system-overview.md) - Maps the BotFarm web host, reusable libraries, and bot modules, then traces how the minimal-hosting entry point composes services, discovers bots at build time, runs hosted services, and stores runtime data.
