# Quickstart: Validating Admin-Managed Bot Settings

This guide shows the feature working end to end. Stored fields and states are in
[data-model.md](data-model.md), and the service, dashboard and log behavior is in
[contracts/bot-settings.md](contracts/bot-settings.md).

## Prerequisites

- .NET 10 SDK.
- Docker, for MongoDB and the integration tests.
- A Telegram bot token from @BotFather for TestBot, a chat to receive admin alerts, and a reachable
  webhook (Dev Tunnel, ngrok or LocalTunnel), as for normal local development.
- A second token for a different Telegram bot, for the account change and duplicate checks.

## 1. Automated checks

Unit tests cover validation, the settings service, options wiring,
client recreation, coordinator states and the dashboard components.

```bash
dotnet test tests/BotFarm.Core.UnitTests
```

```bash
dotnet test tests/BotFarm.Shared.UnitTests
```

```bash
dotnet test tests/BotFarm.UnitTests
```

```bash
dotnet test tests/TestBot.UnitTests
```

Integration tests cover the MongoDB settings store and host startup with missing settings and with
settings only in configuration. They start MongoDB in Docker.

```bash
dotnet test tests/BotFarm.IntegrationTests
```

## 2. Fresh installation (User Story 1)

1. Remove TestBot's `Token`, `AdminChatId` and `WebhookSecret` from `appsettings.Development.json`
   and user secrets, and drop the `BotSettings` and `BotControlStates` collections from the
   `BotFarmControl` database.
2. Start the application. Expected: it starts, TestBot shows "Not configured" and its enable switch is
   disabled; an update posted to `/api/TestBot/update` gets HTTP 503. TestBot's webhook card, and
   sending a message from its chats card if it has chats, show a "not configured" message instead of
   an error.
3. Open TestBot's settings, enter the token and admin chat, and save. Expected: the save confirmation
   names TestBot's Telegram username, the card shows the bot ID, and TestBot stays disabled.
4. Enable TestBot. Expected: it receives updates without a restart, and the card shows its Telegram
   username.
5. Change the admin chat and trigger an admin alert (for example a command that fails). Expected: the
   alert arrives in the new chat.
6. Set a custom webhook secret. Expected: TestBot is re-applied and Telegram's webhook info shows no
   delivery errors for new updates.
7. Try a malformed token, a revoked token and the token of a bot already used by another registered
   bot. Expected: each save is rejected with a message naming the problem; TestBot keeps working.
8. Enter the second bot's token. Expected: a confirmation shows both bot IDs; after confirming, the
   old account's webhook is removed and the new account receives updates.

## 3. Secrets stay hidden (User Story 2)

1. In MongoDB, read the `BotSettings` document for TestBot. Expected: no readable token or webhook
   secret, only protected values and the bot ID.
2. Search `logs/` and the OTLP receiver's output for the token's secret part and the webhook
   secret. Expected: no matches.
3. View the dashboard page source and the Blazor circuit messages in the browser developer tools
   while the settings dialog is open. Expected: neither value appears.
4. Run a TestBot backup and search the archive. Expected: no settings.

## 4. Upgrade with saved bot states (User Story 3)

1. Drop the `BotSettings` collection, keep the `BotControlStates` document with TestBot enabled, and
   put a valid `Token`, `AdminChatId` and `Enabled=false` into configuration.
2. Start the application. Expected: TestBot shows "Not configured" and gets HTTP 503; the
   configuration values are not used.
3. Enter the token and admin chat on the settings card. Expected: TestBot returns to its saved
   enabled state and shows its Telegram username.
4. Drop both collections and start. Expected: TestBot is "Not configured"; after its settings are
   entered it stays disabled until an administrator enables it.

## 5. Unreadable settings (User Story 4)

1. Stop the application, move the `dpkeys` folder out of the data folder, and start again.
   Expected: the application starts, TestBot shows "Not configured", its settings card says "Token
   must be re-entered", it gets HTTP 503, and an error log names the setting.
2. Enter the token again. Expected: TestBot returns to its saved enabled state.

## 6. Degraded store

1. Stop MongoDB and start the application. Expected: the dashboard opens for an already signed-in
   session, TestBot is unknown and gated, and no configuration values are used.
2. Start MongoDB and use Retry. Expected: settings and enabled state load and are applied.
