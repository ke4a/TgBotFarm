---
type: integration
title: Telegram Webhook Integration
description: Explains how durable bot lifecycle state drives Telegram webhook setup and pause, how development or static URL resolvers select the endpoint, and how inbound updates are gated.
tags: [integration, telegram, webhooks]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-06T14:43:11.351Z
sources:
  - id: openwiki-source-4726dde41bf8fa012078aa9f
    resource: repo://BotFarm.Core/Abstractions/BotService.cs
  - id: openwiki-source-48906944d9d67f8e9f27c2b4
    resource: repo://BotFarm.Core/Abstractions/UpdateService.cs
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-eac1d06e4926176426695fe7
    resource: repo://BotFarm.Core/Services/BotControlCoordinator.cs
  - id: openwiki-source-1d926748a20c3e7b9c558fe5
    resource: repo://BotFarm.Core/Services/BotWebhookInitializerService.cs
  - id: openwiki-source-690fe93b9a60304c981f08d3
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/DevTunnelWebhookUrlResolver.cs
  - id: openwiki-source-e457059d4a7df0aecbe849d8
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/LocalTunnelWebhookUrlResolver.cs
  - id: openwiki-source-19e12a2587c9394abe699e13
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/NgrokWebhookUrlResolver.cs
  - id: openwiki-source-2a6cbd0aae5db70f8ee8dcaa
    resource: repo://BotFarm.Core/Services/WebhookUrlResolvers/StaticWebhookUrlResolver.cs
  - id: openwiki-source-9e5e9e7e527f1876c29847e4
    resource: repo://BotFarm/Program.cs
  - id: openwiki-source-5a967c2a70e92f59eae7aebf
    resource: repo://BotFarm/Startup.cs
  - id: openwiki-source-254c9b517d5aa3409beaff97
    resource: repo://TestBot/Controllers/UpdateController.cs
  - id: openwiki-source-87c5f84a91fd30f5c467624e
    resource: repo://tests/BotFarm.Core.UnitTests/Services/BotWebhookInitializerServiceTests.cs
  - id: openwiki-source-93dcf560f99a80fa893d658e
    resource: repo://tests/BotFarm.Core.UnitTests/Services/WebhookUrlResolvers/NgrokWebhookUrlResolverTests.cs
  - id: openwiki-source-486ac74c9ea2a7218e734493
    resource: repo://tests/BotFarm.Core.UnitTests/Services/WebhookUrlResolvers/StaticWebhookUrlResolverTests.cs
generated: { by: "copilot", at: "2026-10-06T14:43:11.351Z" }
---

# Telegram Webhook Integration

## Startup and per-bot behavior

`Program.Main` starts scheduled jobs and then the web host. After the host starts, it calls `BotControlCoordinator.InitializeAsync`, which loads each bot's durable desired state and applies it through `IBotWebhookInitializer`. An enabled bot follows the enable path; a disabled bot follows the pause path. This makes the stored control state, rather than an in-memory configuration snapshot, determine startup webhook behavior. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) for state persistence, retries, and status transitions.

Each enable operation initializes the bot, resolves the configured `WebHookUrl`, then sets a bot-specific endpoint:

```text
<base-url>/api/<bot-name>/update
```

Resolver selection uses the first registered resolver whose `CanResolve` returns true. Resolution happens for each enable operation; no shared resolved URL is cached across bots. If no resolver matches, the initializer throws and the coordinator retries the transition. After its retry limit, the bot is marked `Error` and its inbound update gate stays closed; the host is not held before HTTP startup waiting for a webhook to resolve.

## Resolver selection

`AddCoreServices` registers tunnel resolvers only in Development, in this order: Visual Studio Dev Tunnels, localtunnel, then ngrok. `StaticWebhookUrlResolver` is registered afterward in all environments as the catch-all fallback. Its input is trimmed of trailing slashes and must be an absolute HTTPS URL.

| `WebHookUrl` value | Resolver behavior |
| --- | --- |
| `devtunnel` | Reads `VS_TUNNEL_URL`; a missing or blank value fails. |
| `localtunnel` | Reads `LOCALTUNNEL_URL`; a missing or blank value fails. |
| `ngrok` | Queries the ngrok inspection API at `NGROK_API_URL`, defaulting to `http://ngrok:4040/api/tunnels`. It retries up to ten times with a three-second delay and prefers an HTTPS public tunnel when present. |
| A literal URL | Uses the static fallback, which accepts an absolute HTTPS URL and removes a trailing slash. |

The ngrok resolver chooses the first HTTPS tunnel when available, otherwise the first reported tunnel. The static resolver's HTTPS validation does not run for an ngrok result, so deployment should ensure ngrok reports a Telegram-compatible HTTPS public URL.

## Update delivery

The host registers MVC controllers and Telegram's MVC integration, then maps controllers as endpoints. TestBot's `UpdateController` is bound to `POST /api/TestBot/Update`; it resolves the keyed `IUpdateService` and checks its control gate before forwarding the received Telegram `Update`. While that bot is not confirmed `Applied` and enabled, the endpoint returns HTTP 503 without processing the update. The casing differs from the generated webhook path only in `update` versus `Update`; ASP.NET Core routes are case-insensitive. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) for dispatch from the controller into command, callback, and message handlers.

## Pause, resume, and failures

`BotService.InitializeWebHook` calls Telegram's `SetWebhook` and remembers the URL for recovery. `Pause` deletes the Telegram webhook and returns false after logging for a non-cancellation failure. `Resume` reapplies the saved URL; if Telegram rejects the recovery request, the service logs the failure and asks the host to stop. Caller-requested cancellation is propagated by both operations.

The coordinator treats webhook setup or pause failures as transition failures: it retries, then reports an error state rather than claiming that the requested state was applied. The operator can explicitly retry from the dashboard. `BotService.Pause` logs and returns false when deleting the webhook fails; `Resume` reapplies the remembered webhook URL and asks the host to stop if recovery fails. Operational configuration and container setup are covered in [Deployment and Operations](../operations/deployment.md).

## Focused tests

`BotWebhookInitializerServiceTests` verifies that enabling initializes the bot before resolving and setting its webhook, that disabling delegates to pause, that the first matching resolver wins, and that no matching resolver fails. Resolver tests cover HTTPS validation and slash trimming for static URLs, environment-variable requirements for Dev Tunnels/localtunnel, and ngrok tunnel preference/retries. See [Testing Strategy](../testing/strategy.md) for the full test-project map.
