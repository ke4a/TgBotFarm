---
type: workflow
title: Operator Dashboard and Administration
description: Covers first-run admin setup with a one-time setup code, sign-in, the dashboard layout and theme preference, per-bot dashboards, the host dashboard and shutdown API, bot lifecycle controls, and the health UI authentication boundary.
tags: [workflow, dashboard, authentication, operations]
sources:
  - id: openwiki-source-b4d82fa7a6133507fa36a6e9
    resource: repo://BotFarm.Shared/Components/BotDashboard.razor
  - id: openwiki-source-2ddb7b937e2b26cdeda7f71c
    resource: repo://BotFarm.Shared/Components/BotEnabledControl.razor
  - id: openwiki-source-ca4d5d5321987a1bf79ec003
    resource: repo://BotFarm.Shared/Components/BotEnabledControl.razor.cs
  - id: openwiki-source-cba947be3bcfc1b9a50791ff
    resource: repo://BotFarm.Shared/Components/DashboardBackups.razor
  - id: openwiki-source-52e40ce88db44179582646ea
    resource: repo://BotFarm.Shared/Components/DashboardBackups.razor.cs
  - id: openwiki-source-275e8e00b391221890338e88
    resource: repo://BotFarm.Shared/Components/DashboardChats.razor
  - id: openwiki-source-13bd16102154f92ecad49925
    resource: repo://BotFarm.Shared/Components/DashboardChats.razor.cs
  - id: openwiki-source-01852889bc7b895b7c5e196a
    resource: repo://BotFarm.Shared/Components/DashboardWebhook.razor.cs
  - id: openwiki-source-48fc33ae87ba94d3e4523aba
    resource: repo://BotFarm.Shared/Components/SendMessageDialog.razor.cs
  - id: openwiki-source-dc25091fe31bca5c52af6e0a
    resource: repo://BotFarm.Shared/Components/SendMessageDialog.razor.js
  - id: openwiki-source-5328901543edbdcc02b894c1
    resource: repo://BotFarm.Shared/MainLayout.razor
  - id: openwiki-source-8a8fc7855cb8c13e5c134c88
    resource: repo://BotFarm.Shared/MainLayout.razor.cs
  - id: openwiki-source-c96ad697daff4f9b905211c2
    resource: repo://BotFarm.Shared/Utilities/FormatUtils.cs
  - id: openwiki-source-61077e32741b30cd2c00b401
    resource: repo://BotFarm/App.razor
  - id: openwiki-source-0e9b6f18f84f6f68e3112cf4
    resource: repo://BotFarm/Authentication/ApiKeyAuthenticationHandler.cs
  - id: openwiki-source-fea3c9751b25c983e22882a4
    resource: repo://BotFarm/Authentication/DevelopmentAuthenticationHandler.cs
  - id: openwiki-source-3166064184e0fbde998ec99f
    resource: repo://BotFarm/Authentication/SetupCodeService.cs
  - id: openwiki-source-b9749a4f6389f176785ce4b1
    resource: repo://BotFarm/Components/LogViewerDialog.razor.cs
  - id: openwiki-source-cfc624c25ff4f6f9a230250a
    resource: repo://BotFarm/Controllers/DashboardController.cs
  - id: openwiki-source-16cd45085d0d6ec961432a0a
    resource: repo://BotFarm/Extensions/HealthCheckExtensions.cs
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-8f7039ecdaca4e666f599833
    resource: repo://BotFarm/HostedServices/SetupAnnouncementHostedService.cs
  - id: openwiki-source-12245c66c70c78d95c593000
    resource: repo://BotFarm/Logging/LogFileTail.cs
  - id: openwiki-source-d7a307bf5764d5ffcff4415d
    resource: repo://BotFarm/Pages/Account/Login.cshtml.cs
  - id: openwiki-source-edde3993c06ab7f73e84221a
    resource: repo://BotFarm/Pages/Account/Logout.cshtml.cs
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
  - id: openwiki-source-77307a8a18c3819d94233421
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/BotDashboardRenderTests.cs
  - id: openwiki-source-762a1b8edbdee0cefbc132ce
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/BotEnabledControlTests.cs
  - id: openwiki-source-3af48019bf47409f851d1595
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardBackupsTests.cs
  - id: openwiki-source-b1f2fc86ee9cc2aabfa4426b
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs
  - id: openwiki-source-55443d04a10e8ae9a579c4ce
    resource: repo://tests/BotFarm.Shared.UnitTests/Components/DashboardWebhookRenderTests.cs
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
generated: { by: "claude-code", at: "2026-11-12T10:04:59.900Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T10:04:59.900Z
---

# Operator Dashboard and Administration

## First-run setup

Razor Pages under `/Account` are anonymous; every other page and the Razor components require an authenticated user. `LoginModel` redirects to `/Account/Setup` while no Identity user exists, and setup redirects back to login once one does.

Creating the first admin requires a **setup code**. `SetupCodeService` holds it in memory only. It takes the code from `Setup:Code` (env `Setup__Code`, at least 16 characters, otherwise startup fails without echoing the value) or generates a random 16-byte base64url code each time the process starts. `SetupAnnouncementHostedService` checks the identity store at startup. When no user exists, it logs a warning with the generated code, or, for a configured code, a reminder naming the setting without its value. Read the code from the container or host log. If the identity database is unreachable, the service logs a warning without the code and does not block startup.

`SetupModel.OnPost` verifies the code first, so a visitor without it learns nothing about the other fields, and logs a rejected attempt without the submitted value. Then it checks that the username and password are present and that the confirmation matches. `CompleteSetupAsync` serializes attempts: inside a lock it re-checks that no user exists and verifies the code again. It creates the user and then erases the code, so concurrent or repeated requests cannot create a second admin. Identity validation errors are shown on the page, and the code stays valid. The new user is signed in persistently and sent to `/`.

The sign-in and setup pages are shown in the operator's language, with a row of language links at the top; validation, lockout, setup-code and Identity password errors are translated too, while log messages stay in English. See [Admin UI Localization](../architecture/admin-localization.md).

## Sign-in and sessions

In Production, the Identity application cookie authenticates interactive requests. It lasts 14 days with sliding expiration, and login, logout, and access-denied all go to the account pages. Signing out happens only on an antiforgery-validated `POST /Account/Logout`, sent by the layout's sign-out button; a `GET` of that page just redirects home, so links or embedded resources cannot end the session. Passwords need at least eight characters with an uppercase and a non-alphanumeric character, and five failed attempts lock the account for 15 minutes. Login shows a generic error for bad credentials and a lockout message when locked out, and only redirects to local return URLs. In Development, a development authentication handler supplies a synthetic identity. There is no role-based access: any signed-in user is an operator. Data-protection keys persist under the data folder's `dpkeys`, so cookies stay valid across restarts when that folder is persisted.

## Layout and bot dashboards

The shared `MainLayout` has a dense app bar (menu toggle, logo, page title, language picker, dark-mode toggle, and sign-out button) and a responsive drawer linking to Home, the host Dashboard, the Health UI, and, under a Bots label, one page per registered bot module (`/<BotName>/dashboard`, with the bot's configured emoji or a robot icon). Pages fill the app bar's `LogoSection` and `TitleSection` outlets and set a browser `PageTitle`; each page also has a visually hidden `h1`. The layout's `MudTheme` defines light and dark palettes with a single violet accent, Inter typography, and 10px rounded corners; `site.css` styles the cards, tables, and editor toolbar to match. The dark-mode preference is kept in a `darkModePreference` cookie: `App.razor` reads it server-side so the first render uses the right theme. Without the cookie, the layout asks the browser for its system preference after the first render and stores it. The toggle updates the cookie. The language picker lists the supported languages, marks the current one, and switches by reloading the page through `/culture/set`, which stores the choice in a cookie; `App.razor` sets the page's `lang` attribute to the current UI language. All layout, dashboard, card and dialog texts, including MudBlazor's built-in texts such as the table pager, follow that language.

A bot's dashboard (TestBot's is `/testbot/dashboard`) renders the shared `BotDashboard` layout with the bot's name, so every bot gets the same `BotFarm.Shared` cards, which resolve that bot's services from `IBotRegistry` by name: backups and chats in the wide column, the webhook card and stats in the narrow one. `BotDashboard` also shows the bot name as the app bar title, the optional logo (with the bot name as alt text) and an optional footer. A bot can swap the stats card for its own through `Stats`, append cards to either column through `MainExtra` and `SideExtra`, and add full-width grid items as child content; see [Adding a Bot Module](adding-a-bot.md).

- **Backups** lists archives newest-first with a row number and supports create, download, delete, and restore. Delete and restore need confirmation, and results and errors appear as notifications. While creating, the button becomes a progress indicator and duplicate requests are ignored. Backup and restore pause the bot through the coordinator; see [Persistence and Backups](../architecture/persistence-and-backups.md).
- **Chats** loads the list of known chat IDs from the bot database once (and again on Refresh), then pages through it with a server-side table (10 rows by default; 10, 25, 50, or 100 per page). Telegram chat details are fetched only for rows on the current page whose names are not cached yet, at most five at a time; rows keep their order and numbering whatever order the fetches finish in. Fetched names are cached until Refresh clears them. A failed lookup is logged and shown as an "Unavailable" row with its send action disabled, while the rest of the page still loads; it is not cached, so it is retried on the next load. A cancelled page request returns no rows. Names longer than 45 user-perceived characters (`MaxNameLength`) are cut with an ellipsis by `FormatUtils.Truncate`, which never splits an emoji, and the full name appears in a tooltip. Hovering a name or ID cell reveals a button that copies the value to the clipboard (always visible on touch devices); copying a shortened name copies the full name. Unavailable chats have no name copy button.
- **Delete chat** (per row) asks for confirmation, then calls `IDatabaseService.DeleteChat`, which removes the chat's settings and the bot's own data for it, and reloads the list. **Clean up** (card header) checks every known chat with Telegram, five at a time with a progress bar, and counts the chats the bot can no longer reach (kicked, blocked, or deleted chats, per `ChatReachability`). If there are any, it asks for confirmation naming how many of how many chats will go, then deletes them one by one and reports full or partial success. Chats whose check failed for another reason are kept and mentioned in the messages. See [Persistence and Backups](../architecture/persistence-and-backups.md#chat-deletion).
- **Send message** opens `SendMessageDialog` for one chat. **Send to all** (card header) opens the same editor titled with the chat count; its Send asks for a second confirmation before the dialog closes. The message then goes out through `IBroadcastService` with Telegram-friendly pacing and a progress bar, and the result reports how many chats received it and how many were unreachable or failed. A second broadcast for the same bot is refused while one runs. Once started, a broadcast finishes even if the page is closed; only the progress and result display are dropped. See [Telegram Webhook Integration](../integrations/telegram-webhooks.md#outbound-broadcasts-and-chat-reachability). The dialog imports its own JS module, which creates a Quill editor restricted to formats Telegram HTML supports (bold, italic, underline, strikethrough, link, and a `tg-spoiler` spoiler). On Send, the module turns the content into Telegram-compatible HTML (paragraphs become newlines), and the dialog sends it through `INotificationService`, or, when a caller passes a `SubmitHandler` (as Send to all does), hands it to the handler and closes with the message only if the handler accepts. An empty message shows a warning and a failed send shows an error, both leaving the dialog open; a second Send is ignored while one is in flight, and the buttons show progress. The module is disposed with the dialog.
- **Webhook** shows the bot's on/off switch (the same `BotEnabledControl` as on the host dashboard) and Telegram's webhook info: the URL with a copy button, the pending update count, and the last delivery error. If the info cannot be read, the card says so. **Drop pending updates** is enabled only when updates are queued and the bot is not applying a change; after confirmation it calls `IBotControlService.DropPendingUpdatesAsync`, shows a busy notice if another operation holds the bot, and reloads the info. Turning the bot on or off from the card reloads the info when the command finishes, since that registers or removes the webhook. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md#webhook-inspection-and-pending-updates).
- **Stats** loads the chat count and database statistics concurrently, reports load errors, and always clears its loading state.

## Host dashboard and shutdown

`/dashboard` shows memory and uptime from the health checks and lists log files from the data folder's `logs` directory, newest first with a row number, which can be viewed or downloaded. The log viewer groups multi-line entries such as stack traces, highlights entries by level, filters to all entries, warnings and above, or errors only, and renders at most the newest 5,000 matching entries, saying how many older ones are hidden. For a current (not archived) log file it follows new entries every three seconds and keeps the view pinned to the bottom; the live tail can be paused. Its shutdown button asks for confirmation and stops the host immediately, without pausing webhooks.

The authenticated `POST /api/dashboard/shutdown` endpoint accepts an optional `pauseBotUpdates` body flag. When it is true, the controller calls `IBotControlService.PauseAllForShutdownAsync`, which deletes each bot's webhook and leaves the bot gated before it stops the host. It returns a success-shaped response without waiting for the process to exit. Scheduled shutdowns are in [Configuration and Operations](../operations/deployment.md).

## Bot lifecycle controls

The host dashboard lists each bot with the shared `BotEnabledControl`, which shows the desired target and runtime status; the bot webhook card reuses it. The switch sends an explicit enabled/disabled target to `IBotControlService.SetEnabledAsync`. It is disabled while the bot is busy or its state is unknown, pending, errored, or has no confirmed desired value, and a pending action shows an applying indicator.

Unknown or error states offer **Retry**. Unknown with a desired value means saving the command was not confirmed, so Retry saves and applies it. Unknown without a desired value reloads and applies the saved state. Error means the saved state could not be applied after several attempts. A maintenance pause whose resume fails also ends in Error and sends an admin alert. A second click while a command runs is ignored. Action failures are logged with the bot name and exception type, and the control shows an explanatory message rather than exception details; the message stays until the reloaded status is known again. A concurrent operation on the same bot shows a busy notice, while different bots stay independent. The control tells its parent when a command starts and when it finishes, so the parent reloads the status. Desired state is durable in MongoDB; status is per process. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md).

## Health UI authentication boundary

`/health` and `/health-ui` require `HEALTH_CHECKS_UI_POLICY`: an authenticated user via the interactive scheme (or the development scheme) or the `ApiKey` scheme. A key generated once per process is attached by the HealthChecks UI as `X-Api-Key` when it polls `/health`. It is for that internal call only, not a login credential. `/healthz` is the anonymous liveness probe and reveals no details.

## Focused tests

`SetupCodeServiceTests`, `SetupAnnouncementHostedServiceTests`, and `SetupModelTests` cover code generation and configuration, checking the code first, invalidating it after use, concurrent setup, and existing-user redirects. `LoginModelTests` cover the first-run redirect, return URLs, invalid credentials, and lockout, and both account models return Russian errors when the UI is Russian; `LogoutModelTests` cover POST-only sign-out. `DashboardControllerTests` check pausing bots before shutdown. `BotEnabledControlTests`, `DashboardBotControlTests` and the bUnit `DashboardBotControlRenderTests` cover status feedback, toggles, retries, and switch availability. The shared component and `MainLayoutTests` cover backups, chats (including name caching, deletion, clean-up and broadcasts), the webhook card, the send-message dialog, stats, the language picker, translated layout labels, and the cookie-based theme. `LogViewerDialogRenderTests` cover the log viewer's highlighting, filter, live tail, and entry cap. See [Testing Strategy](../testing/strategy.md).
