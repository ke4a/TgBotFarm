---
type: workflow
title: Operator Dashboard and Administration
description: Covers first-run admin setup with a one-time setup code, sign-in, the dashboard layout and theme preference, per-bot dashboards, the host dashboard and shutdown API, bot lifecycle controls, and the health UI authentication boundary.
tags: [workflow, dashboard, authentication, operations]
sources:
  - id: openwiki-source-cba947be3bcfc1b9a50791ff
    resource: repo://BotFarm.Shared/Components/DashboardBackups.razor
  - id: openwiki-source-52e40ce88db44179582646ea
    resource: repo://BotFarm.Shared/Components/DashboardBackups.razor.cs
  - id: openwiki-source-275e8e00b391221890338e88
    resource: repo://BotFarm.Shared/Components/DashboardChats.razor
  - id: openwiki-source-13bd16102154f92ecad49925
    resource: repo://BotFarm.Shared/Components/DashboardChats.razor.cs
  - id: openwiki-source-5328901543edbdcc02b894c1
    resource: repo://BotFarm.Shared/MainLayout.razor
  - id: openwiki-source-8a8fc7855cb8c13e5c134c88
    resource: repo://BotFarm.Shared/MainLayout.razor.cs
  - id: openwiki-source-61077e32741b30cd2c00b401
    resource: repo://BotFarm/App.razor
  - id: openwiki-source-0e9b6f18f84f6f68e3112cf4
    resource: repo://BotFarm/Authentication/ApiKeyAuthenticationHandler.cs
  - id: openwiki-source-fea3c9751b25c983e22882a4
    resource: repo://BotFarm/Authentication/DevelopmentAuthenticationHandler.cs
  - id: openwiki-source-3166064184e0fbde998ec99f
    resource: repo://BotFarm/Authentication/SetupCodeService.cs
  - id: openwiki-source-cfc624c25ff4f6f9a230250a
    resource: repo://BotFarm/Controllers/DashboardController.cs
  - id: openwiki-source-16cd45085d0d6ec961432a0a
    resource: repo://BotFarm/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-8f7039ecdaca4e666f599833
    resource: repo://BotFarm/HostedServices/SetupAnnouncementHostedService.cs
  - id: openwiki-source-d7a307bf5764d5ffcff4415d
    resource: repo://BotFarm/Pages/Account/Login.cshtml.cs
  - id: openwiki-source-54f57d8ffb92cfc1015f6a01
    resource: repo://BotFarm/Pages/Account/Setup.cshtml.cs
  - id: openwiki-source-1590161f1d234ac12e5e01f0
    resource: repo://BotFarm/Pages/Dashboard.razor
  - id: openwiki-source-0e78ef6b98d18c9d1f8bff7e
    resource: repo://BotFarm/Pages/Dashboard.razor.cs
  - id: openwiki-source-f1ddfbc7325e9c927245d0bd
    resource: repo://BotFarm/wwwroot/css/site.css
  - id: openwiki-source-cd0c76fca10dd801c7c05182
    resource: repo://BotFarm/wwwroot/js/site.js
  - id: openwiki-source-66cfed0bff7015b74aadf547
    resource: repo://TestBot/Pages/TestBotDashboard.razor
  - id: openwiki-source-3af48019bf47409f851d1595
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardBackupsTests.cs
  - id: openwiki-source-b1f2fc86ee9cc2aabfa4426b
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs
  - id: openwiki-source-589566a8335d3d25db40fe52
    resource: repo://tests/BotFarm.UnitTests/Authentication/SetupCodeServiceTests.cs
  - id: openwiki-source-650fcd1aa895f9944337719d
    resource: repo://tests/BotFarm.UnitTests/Controllers/DashboardControllerTests.cs
  - id: openwiki-source-a1afafc6c31904d5b8db18f6
    resource: repo://tests/BotFarm.UnitTests/HostedServices/SetupAnnouncementHostedServiceTests.cs
  - id: openwiki-source-a9825a65f9f1dcaadd450586
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/LoginModelTests.cs
  - id: openwiki-source-f58a8af365797e8a1d53e668
    resource: repo://tests/BotFarm.UnitTests/Pages/Account/SetupModelTests.cs
  - id: openwiki-source-a553b821cb052209d4dbdfa1
    resource: repo://tests/BotFarm.UnitTests/Pages/DashboardBotControlTests.cs
generated: { by: "claude-code", at: "2026-11-05T21:28:06.094Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-05T21:28:06.094Z
---

# Operator Dashboard and Administration

## First-run setup

Razor Pages under `/Account` are anonymous; every other page and the Razor components require an authenticated user. `LoginModel` redirects to `/Account/Setup` while no Identity user exists, and setup redirects back to login once one does.

Creating the first admin requires a **setup code**. `SetupCodeService` holds it in memory only. It takes the code from `Setup:Code` (env `Setup__Code`, at least 16 characters, otherwise startup fails without echoing the value) or generates a random 16-byte base64url code each time the process starts. `SetupAnnouncementHostedService` checks the identity store at startup. When no user exists, it logs a warning with the generated code, or, for a configured code, a reminder naming the setting without its value. Read the code from the container or host log. If the identity database is unreachable, the service logs a warning without the code and does not block startup.

`SetupModel.OnPost` verifies the code first, so a visitor without it learns nothing about the other fields, and logs a rejected attempt without the submitted value. Then it checks that the username and password are present and that the confirmation matches. `CompleteSetupAsync` serializes attempts: inside a lock it re-checks that no user exists and verifies the code again. It creates the user and then erases the code, so concurrent or repeated requests cannot create a second admin. Identity validation errors are shown on the page, and the code stays valid. The new user is signed in persistently and sent to `/`.

## Sign-in and sessions

In Production, the Identity application cookie authenticates interactive requests. It lasts 14 days with sliding expiration, and login, logout, and access-denied all go to the account pages. Passwords need at least eight characters with an uppercase and a non-alphanumeric character, and five failed attempts lock the account for 15 minutes. Login shows a generic error for bad credentials and a lockout message when locked out, and only redirects to local return URLs. In Development, a development authentication handler supplies a synthetic identity. There is no role-based access: any signed-in user is an operator. Data-protection keys persist under the data folder's `dpkeys`, so cookies stay valid across restarts when that folder is persisted.

## Layout and bot dashboards

The shared `MainLayout` links to Home, the host Dashboard, one page per registered bot module (`/<BotName>/dashboard`, with the bot's configured emoji), the Health UI, and Logout. The dark-mode preference is kept in a `darkModePreference` cookie: `App.razor` reads it server-side so the first render uses the right theme. Without the cookie, the layout asks the browser for its system preference after the first render and stores it. The toggle updates the cookie.

A bot's dashboard (TestBot's is `/testbot/dashboard`) composes three `BotFarm.Shared` components that resolve that bot's services from `IBotRegistry` by name:

- **Backups** lists archives newest-first and supports create, download, delete, and restore. Delete and restore need confirmation, and results and errors appear as notifications. While creating, the button becomes a progress indicator and duplicate requests are ignored. Backup and restore pause the bot through the coordinator; see [Persistence and Backups](../architecture/persistence-and-backups.md).
- **Chats** loads the list of known chat IDs from the bot database once (and again on Refresh), then pages through it with a server-side table (10 rows by default; 10, 25, 50, or 100 per page). Telegram chat details are fetched only for the rows on the current page. A failed lookup is logged and shown as an "Unavailable" row with its send action disabled, while the rest of the page still loads; a cancelled page request returns no rows. Hovering a name or ID cell reveals a button that copies the value to the clipboard (always visible on touch devices). The operator can compose a rich-text message and send it to a chat through `INotificationService` as HTML.
- **Stats** loads the chat count and database statistics concurrently, reports load errors, and always clears its loading state.

## Host dashboard and shutdown

`/dashboard` shows memory and uptime from the health checks and lists log files from the data folder's `logs` directory, which can be viewed or downloaded. Its shutdown button asks for confirmation and stops the host immediately, without pausing webhooks.

The authenticated `POST /api/dashboard/shutdown` endpoint accepts an optional `pauseBotUpdates` body flag. When it is true, the controller calls `IBotControlService.PauseAllForShutdownAsync`, which deletes each bot's webhook and leaves the bot gated before it stops the host. It returns a success-shaped response without waiting for the process to exit. Scheduled shutdowns are in [Configuration and Operations](../operations/deployment.md).

## Bot lifecycle controls

The host dashboard lists each bot's desired target and runtime status. The switch sends an explicit enabled/disabled target to `IBotControlService.SetEnabledAsync`. It is disabled while the bot is busy or its state is unknown, pending, errored, or has no confirmed desired value, and a pending action shows an applying indicator.

Unknown or error states offer **Retry**. Unknown with a desired value means saving the command was not confirmed, so Retry saves and applies it. Unknown without a desired value reloads and applies the saved state. Error means the saved state could not be applied after several attempts. A maintenance pause whose resume fails also ends in Error and sends an admin alert. Action failures are logged with the bot name and exception type, and the UI shows an explanatory message rather than exception details. A concurrent operation on the same bot shows a busy notice, while different bots stay independent. Desired state is durable in MongoDB; status is per process. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md).

## Health UI authentication boundary

`/health` and `/health-ui` require `HEALTH_CHECKS_UI_POLICY`: an authenticated user via the interactive scheme (or the development scheme) or the `ApiKey` scheme. A key generated once per process is attached by the HealthChecks UI as `X-Api-Key` when it polls `/health`. It is for that internal call only, not a login credential. `/healthz` is the anonymous liveness probe and reveals no details.

## Focused tests

`SetupCodeServiceTests`, `SetupAnnouncementHostedServiceTests`, and `SetupModelTests` cover code generation and configuration, checking the code first, invalidating it after use, concurrent setup, and existing-user redirects. `LoginModelTests` cover the first-run redirect, return URLs, invalid credentials, and lockout. `DashboardControllerTests` check pausing bots before shutdown. `DashboardBotControlTests` cover status feedback, toggles, retries, and switch availability. The shared component and `MainLayoutTests` cover backups, chats, stats, and the cookie-based theme. See [Testing Strategy](../testing/strategy.md).
