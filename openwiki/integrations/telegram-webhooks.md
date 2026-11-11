---
type: integration
title: Telegram Webhook Integration
description: Explains how BotFarm resolves webhook URLs, authenticates Telegram deliveries with a per-bot secret token, suppresses duplicates, retries temporary failures through Telegram redelivery, and throttles admin alerts.
tags: [telegram, webhooks, integration, security]
sources:
  - id: openwiki-source-6f7dea2a166b86ef9000b099
    resource: repo://BotFarm.Core/Base/BotService.cs
  - id: openwiki-source-60145f991035e51f8a6872fc
    resource: repo://BotFarm.Core/Base/UpdateService.cs
  - id: openwiki-source-97e8c28339d027a055eb5bf6
    resource: repo://BotFarm.Core/Controllers/UpdateControllerBase.cs
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-07e0d287df62a45d462d1e16
    resource: repo://BotFarm.Core/Helpers/ChatReachability.cs
  - id: openwiki-source-af0f35b3ce4f52d8a4d0417a
    resource: repo://BotFarm.Core/Helpers/TransientErrors.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-1d926748a20c3e7b9c558fe5
    resource: repo://BotFarm.Core/Services/BotWebhookInitializerService.cs
  - id: openwiki-source-ba905435b28d4d9a14061449
    resource: repo://BotFarm.Core/Services/TelegramBroadcastService.cs
  - id: openwiki-source-c431b57e452c6e5737ea444d
    resource: repo://BotFarm.Core/Services/TelegramNotificationService.cs
  - id: openwiki-source-6c446316689379bd2b784eb5
    resource: repo://BotFarm.Core/Services/UpdateDeliveryGuard.cs
  - id: openwiki-source-ad7162f18047d8868daf8de9
    resource: repo://BotFarm.Core/Services/WebhookSecretProvider.cs
  - id: openwiki-source-690fe93b9a60304c981f08d3
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/DevTunnelWebhookUrlResolver.cs
  - id: openwiki-source-e457059d4a7df0aecbe849d8
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/LocalTunnelWebhookUrlResolver.cs
  - id: openwiki-source-19e12a2587c9394abe699e13
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/NgrokWebhookUrlResolver.cs
  - id: openwiki-source-2a6cbd0aae5db70f8ee8dcaa
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/StaticWebhookUrlResolver.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-e5ee530d12cdb4cc0ab3552a
    resource: repo://BotFarm/HostedServices/BotControlStartupService.cs
  - id: openwiki-source-254c9b517d5aa3409beaff97
    resource: repo://TestBot/Controllers/UpdateController.cs
  - id: openwiki-source-6ada9d260895abaf0ceefdf3
    resource: repo://tests/BotFarm.Core.UnitTests/Helpers/ChatReachabilityTests.cs
  - id: openwiki-source-87c5f84a91fd30f5c467624e
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotWebhookInitializerServiceTests.cs
  - id: openwiki-source-f6dd22010a435d399b31f1d9
    resource: repo://tests/BotFarm.Core.UnitTests/Services/TelegramBroadcastServiceTests.cs
  - id: openwiki-source-fdf959d5f24404f773962876
    resource: repo://tests/BotFarm.Core.UnitTests/Services/UpdateDeliveryGuardTests.cs
  - id: openwiki-source-8a067c64223328391a6f0820
    resource: repo://tests/BotFarm.Core.UnitTests/Services/WebhookSecretProviderTests.cs
  - id: openwiki-source-93dcf560f99a80fa893d658e
    resource: repo://tests/BotFarm.Core.UnitTests/Services/WebhookUrlResolvers/NgrokWebhookUrlResolverTests.cs
  - id: openwiki-source-486ac74c9ea2a7218e734493
    resource: repo://tests/BotFarm.Core.UnitTests/Services/WebhookUrlResolvers/StaticWebhookUrlResolverTests.cs
  - id: openwiki-source-e2012c4a08961c92b5c9c92c
    resource: repo://tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs
generated: { by: "claude-code", at: "2026-11-11T20:41:09.729Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-11T20:41:09.729Z
---

# Telegram Webhook Integration

## Registering a webhook

Webhook transitions are driven by `BotControlCoordinator`, not by configuration snapshots. Once the app has started, `BotControlStartupService` asks the coordinator to apply each bot's saved desired state through `IBotWebhookInitializer`. An enabled bot follows the enable path, and a disabled bot follows the pause path. Operator commands, maintenance pauses, and retries use the same initializer. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) for persistence, retries, and status transitions.

`BotWebhookInitializerService.EnableAsync` initializes the bot (`GetMe`), resolves the configured `WebHookUrl` (a missing value is treated as empty), and calls `SetWebhook` on this endpoint with the bot's secret token:

```text
<base-url>/api/<bot-name>/update
```

`DisableAsync` delegates to `BotService.Pause`, which deletes the webhook and returns `false` after logging any non-cancellation failure. Caller cancellation is passed through every step. Resolution happens on every enable; nothing is cached across bots. If no resolver matches, the initializer throws, and the coordinator retries and eventually marks the bot `Error` with its gate closed. Webhook failures never delay HTTP startup.

## Resolver selection

`AddCoreServices` registers tunnel resolvers only in Development, in this order: Visual Studio Dev Tunnels, localtunnel, then ngrok. `StaticWebhookUrlResolver` is registered after them in all environments as the catch-all fallback. The first resolver whose `CanResolve` returns true wins.

| `WebHookUrl` value | Resolver behavior |
| --- | --- |
| `devtunnel` | Reads `VS_TUNNEL_URL`; a missing or blank value fails. |
| `localtunnel` | Reads `LOCALTUNNEL_URL`; a missing or blank value fails. |
| `ngrok` | Queries the ngrok inspection API at `NGROK_API_URL` (default `http://ngrok:4040/api/tunnels`), retrying up to ten times with a three-second delay. Prefers an HTTPS public tunnel, otherwise the first reported tunnel. |
| A literal URL | Static fallback: trims trailing slashes and requires an absolute HTTPS URL. |

The static resolver's HTTPS check does not apply to ngrok results, so the ngrok agent must report a Telegram-compatible HTTPS URL.

## Webhook secret token

Every `SetWebhook` call passes a secret that Telegram echoes in the `X-Telegram-Bot-Api-Secret-Token` header. `WebhookSecretProvider` returns `Bots:<name>:BotConfig:WebhookSecret` when it is set. Telegram accepts 1–256 characters from `A-Z`, `a-z`, `0-9`, `_`, and `-`, and an invalid value throws a message naming the setting. When no secret is configured, the provider derives a stable one: the unpadded base64url form of HMAC-SHA256, keyed by the bot token, over a fixed purpose string (43 characters). Changing the token therefore changes the derived secret, and the next enable re-registers it. `BotConfigValidator` also rejects an invalid configured secret at startup. The secret is never logged.

## Handling a delivery

Each bot project has a small controller deriving from `UpdateControllerBase` (in `BotFarm.Core.Controllers`). For example, TestBot's `UpdateController` binds `POST /api/TestBot/Update` and its keyed `IUpdateService`; routing is case-insensitive, so it matches the registered `/update` path. The base `Post` action processes a request in this order:

1. **Secret check.** The request must carry exactly one non-empty secret header that matches the expected secret in constant time. Otherwise it logs a warning, without either value, and returns **401**.
2. **Gate check.** If the bot is not `Applied` and enabled, it returns **503** without processing, so Telegram redelivers later.
3. **Duplicate check.** `IUpdateDeliveryGuard.TryAcceptUpdate` records the update ID. An ID already accepted for this bot is acknowledged with **200** and not processed again.
4. **Processing.** The update goes to `IUpdateService.ProcessUpdate`. Success clears any recorded failures and returns **200**.
5. **Temporary failure.** If `TransientErrors.IsTransient` matches the exception, the guard releases the ID so the next delivery is processed. The action logs a warning, alerts, and returns **503** so Telegram retries. On the fifth temporary failure (`MaxTransientAttempts`), it logs an error, alerts that the update was dropped, and returns **200** so one update cannot block the queue behind it.
6. **Permanent failure.** Any other exception is logged and alerted, and the update is acknowledged with **200**.

`TransientErrors` searches the exception chain, including aggregate inner exceptions. It treats MongoDB connection and execution-timeout errors, timeouts, HTTP, socket, and I/O errors, and Telegram API errors with code 429 or 5xx as temporary. A cancellation counts only when it wraps a timeout. Other Telegram rejections are permanent.

Error alerts go through `INotificationService` with the update's message for context. They are limited to one per minute per bot via `TryAcquireAlertSlot`. Sending is best-effort: the notification service logs a failed alert as a warning instead of throwing, so it never changes the response.

## Delivery guard state

`UpdateDeliveryGuard` is a process-local singleton keyed by bot name, using an injected `TimeProvider`. Accepted IDs are remembered for 10 minutes, up to 10,000 per bot. Temporary-failure counts are remembered for 24 hours after the first failure, up to 1,000 per bot. The oldest entries are evicted first. Because the state is in memory, a restart forgets accepted IDs, so Telegram's redelivery after a restart is processed again.

## Inspecting the webhook and dropping pending updates

`IBotControlService.GetWebhookInfoAsync` returns Telegram's `WebhookInfo` for a bot: the registered URL, the pending update count, and the last delivery error. `DropPendingUpdatesAsync` discards the queue without changing whether the bot is enabled. `BotService.DropPendingUpdates` reads the current registration first. If a webhook is set, it calls `SetWebhook` again at the same URL with the bot's secret token and `dropPendingUpdates`, so delivery continues without a gap and keeps its authentication. If none is set, it calls `DeleteWebhook` with `dropPendingUpdates`. The coordinator refuses the call with `BotControlBusyException` while an enable, disable or maintenance pause holds the bot's lock; see [Bot Runtime Lifecycle](../architecture/bot-runtime.md).

## Outbound broadcasts and chat reachability

`IBroadcastService.Broadcast` (implemented by the singleton `TelegramBroadcastService`) sends one Telegram-HTML message to a list of chats, one chat at a time. Sends are spaced 50 ms apart (about 20 per second, under Telegram's limit of about 30 per second to different chats, leaving headroom for normal traffic). A 429 "too many requests" answer waits for the `retry_after` Telegram gives (one second when absent) and retries the same chat, up to three attempts. Any other failure is recorded for that chat and the broadcast moves on. Progress is reported after every chat, cancellation stops before the next chat, and a second broadcast for the same bot is rejected with `InvalidOperationException` while one is running. The `BroadcastResult` holds the number sent, the unreachable chats, and the chats that failed for other reasons.

`ChatReachability.IsUnreachable` decides whether a failed request means the bot has lost the chat for good: a 403 (kicked, blocked, not a member, or deactivated user), or a 400 "chat not found" that is not a group-to-supergroup migration. Temporary and other errors are not treated as unreachable. Broadcasts use it to split unreachable chats from failed ones, and the dashboard's chat clean-up uses it to pick which chats to delete; see [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Focused tests

`UpdateControllerTests` cover the 401 rejection before the readiness check, the 503 gate, duplicate suppression, redelivery after 401/503, transient retries up to the drop limit, failure-count reset, permanent Telegram rejections, and alert throttling. `TelegramNotificationServiceTests` cover a failed alert being logged rather than thrown. `UpdateDeliveryGuardTests` cover windows, per-bot isolation, and capacity eviction. `WebhookSecretProviderTests` and `BotConfigValidatorTests` cover configured and derived secrets. `TransientErrorsTests` covers classification, and `ChatReachabilityTests` covers lost-access versus other errors. `TelegramBroadcastServiceTests` cover pacing, separating unreachable from failed chats, rate-limit waits and the attempt limit, progress, cancellation, and one broadcast per bot. `BotServiceTests` cover dropping pending updates with and without a registered webhook. `BotWebhookInitializerServiceTests` and the resolver tests cover the enable/disable order, resolver precedence, missing resolvers, HTTPS validation, environment variables, and ngrok retries. Operational configuration is in [Configuration and Operations](../operations/deployment.md).
