---
type: workflow
title: Operator Dashboard and Administration
description: Describes first-run account creation, interactive authentication, bot and host dashboards, operational actions, and protected health endpoints.
tags: [workflow, dashboard, administration, authentication]
verified:
  - by: openwiki/0.7.0
    at: 2026-10-03T08:54:46.183Z
sources:
  - id: openwiki-source-cba947be3bcfc1b9a50791ff
    resource: repo://BotFarm.Shared/Components/DashboardBackups.razor
  - id: openwiki-source-52e40ce88db44179582646ea
    resource: repo://BotFarm.Shared/Components/DashboardBackups.razor.cs
  - id: openwiki-source-13bd16102154f92ecad49925
    resource: repo://BotFarm.Shared/Components/DashboardChats.razor.cs
  - id: openwiki-source-5328901543edbdcc02b894c1
    resource: repo://BotFarm.Shared/MainLayout.razor
  - id: openwiki-source-0e9b6f18f84f6f68e3112cf4
    resource: repo://BotFarm/Authentication/ApiKeyAuthenticationHandler.cs
  - id: openwiki-source-fea3c9751b25c983e22882a4
    resource: repo://BotFarm/Authentication/DevelopmentAuthenticationHandler.cs
  - id: openwiki-source-cfc624c25ff4f6f9a230250a
    resource: repo://BotFarm/Controllers/DashboardController.cs
  - id: openwiki-source-16cd45085d0d6ec961432a0a
    resource: repo://BotFarm/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-81baeca6a1a40fc83cf91310
    resource: repo://BotFarm/Health/MemoryHealthCheck.cs
  - id: openwiki-source-d7a307bf5764d5ffcff4415d
    resource: repo://BotFarm/Pages/Account/Login.cshtml.cs
  - id: openwiki-source-54f57d8ffb92cfc1015f6a01
    resource: repo://BotFarm/Pages/Account/Setup.cshtml.cs
  - id: openwiki-source-1590161f1d234ac12e5e01f0
    resource: repo://BotFarm/Pages/Dashboard.razor
  - id: openwiki-source-0e78ef6b98d18c9d1f8bff7e
    resource: repo://BotFarm/Pages/Dashboard.razor.cs
  - id: openwiki-source-5a967c2a70e92f59eae7aebf
    resource: repo://BotFarm/Startup.cs
  - id: openwiki-source-66cfed0bff7015b74aadf547
    resource: repo://TestBot/Pages/TestBotDashboard.razor
  - id: openwiki-source-3af48019bf47409f851d1595
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardBackupsTests.cs
  - id: openwiki-source-b1f2fc86ee9cc2aabfa4426b
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs
  - id: openwiki-source-650fcd1aa895f9944337719d
    resource: repo://tests/BotFarm.UnitTests/Controllers/DashboardControllerTests.cs
  - id: openwiki-source-a9825a65f9f1dcaadd450586
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/LoginModelTests.cs
  - id: openwiki-source-f58a8af365797e8a1d53e668
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/SetupModelTests.cs
generated: { by: "copilot", at: "2026-10-03T08:54:46.183Z" }
---

# Operator Dashboard and Administration

## First-run setup and sign-in

Razor Pages under `/Account` are anonymous so operators can sign in or create the first account. `LoginModel` redirects to setup when no Identity user exists. `SetupModel` creates an account only while the user store is empty, checks that username and password are present and that the password confirmation matches, then signs the new user in. Once a user exists, setup redirects to login.

In Production, the host uses the Identity application cookie for interactive authentication; the cookie lasts 14 days with sliding expiration. Identity password options require at least eight characters, an uppercase character, and a non-alphanumeric character; lockout starts after five failed attempts for 15 minutes. In Development, a development authentication handler supplies a synthetic developer identity. The application does not define role-based dashboard access in these flows.

The shared layout links to Home, the host Dashboard, each registered bot dashboard, the Health UI, and Logout. The current TestBot page is `/testbot/dashboard`.

## Bot dashboard

TestBot's dashboard composes three reusable `BotFarm.Shared` components:

- **Backups** lists local archives and supports create, download, delete, and restore. Delete and restore require confirmation; results and exceptions are shown through dashboard notifications.
- **Chats** reads known chat IDs from the bot database and fetches each chat's Telegram details. It can open a rich-text message dialog and send the message to a selected chat. Individual chat lookup failures are logged while the remaining chats continue to load.
- **Stats** loads chat count, database statistics, and any derived component statistics concurrently.

The shared components receive a bot name and select that bot's database and bot services, which lets the host reuse the same UI for bot-specific dashboards. See [Persistence and Backups](../architecture/persistence-and-backups.md) for storage behavior.

## Host dashboard and shutdown

`/dashboard` shows memory usage and uptime from the health checks, lists log files from the application's `logs` directory, and allows operators to view or download a log. Its shutdown action asks for confirmation and then stops the host; it does not pause bot webhooks first.

The authenticated `POST /api/dashboard/shutdown` endpoint offers a separate optional `pauseBotUpdates` flag. When true, the controller tries to pause each registered bot before stopping the host. The API returns a success-shaped response after requesting shutdown; it does not wait for the process to exit. Scheduled shutdowns and persistence cleanup are described in [Configuration and Operations](../operations/deployment.md).

## Health UI authentication boundary

Both `/health` and `/health-ui` require `HEALTH_CHECKS_UI_POLICY`. The policy requires an authenticated user and permits the interactive authentication scheme plus an `ApiKey` scheme (Development uses its development scheme). A process-local key is generated at startup; the HealthChecks UI attaches it as `X-Api-Key` when polling `/health`. This key is intended only for the UI's internal call, not as a dashboard login credential.

The health checks report process memory and application uptime. Memory reaching its configured threshold reports unhealthy status and sends a warning through the notification service. For endpoint deployment and container probe behavior, see [Configuration and Operations](../operations/deployment.md).

## Focused tests

`SetupModelTests` and `LoginModelTests` cover first-run redirects, account creation, credential validation, successful sign-in, and lockout responses. `DashboardControllerTests` checks host shutdown and optional bot pausing. Shared component tests cover chat loading and backup workflows; see [Testing Strategy](../testing/strategy.md).
