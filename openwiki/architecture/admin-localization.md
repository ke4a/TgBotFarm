---
type: architecture
title: Admin UI Localization
description: How the operator UI (dashboard, bot pages, sign-in and setup) chooses, stores and switches its language, where its JSON translation sets live, and how bot modules and tests plug into it.
tags: [localization, i18n, admin-ui, culture, blazor]
verified:
  - by: openwiki/0.7.0
    at: 2026-11-12T18:30:04.833Z
sources:
  - id: openwiki-source-4f8eb3642edddaf7956b6d05
    resource: repo://BotFarm.Core/Extensions/ServiceCollectionExtensions.cs
  - id: openwiki-source-4f68e476692056e9c7d3db04
    resource: repo://BotFarm.Core/Localization/AcceptLanguageMatchProvider.cs
  - id: openwiki-source-cabbbbc9fd6b9d574025b600
    resource: repo://BotFarm.Core/Localization/AdminLanguageCatalog.cs
  - id: openwiki-source-6fbfd277288bf1f586158d52
    resource: repo://BotFarm.Core/Localization/JsonAdminStringLocalizer.cs
  - id: openwiki-source-ee16bbb41eefa7ab7e600c6e
    resource: repo://BotFarm.Core/Localization/JsonAdminStringLocalizerFactory.cs
  - id: openwiki-source-b4d82fa7a6133507fa36a6e9
    resource: repo://BotFarm.Shared/Components/BotDashboard.razor
  - id: openwiki-source-4d54ab90f322b145293055bc
    resource: repo://BotFarm.Shared/Components/LanguagePicker.razor.cs
  - id: openwiki-source-ab46589d7ae650423477729b
    resource: repo://BotFarm.Shared/Localization/AdminMudLocalizer.cs
  - id: openwiki-source-8e7e36dadb3658bf71310d7d
    resource: repo://BotFarm/Dockerfile
  - id: openwiki-source-5a536b1d1238628d18b1fa5c
    resource: repo://BotFarm/Extensions/WebApplicationExtensions.cs
  - id: openwiki-source-ca231d9e380e508401f566e8
    resource: repo://BotFarm/Localization/AdminLanguageEndpoints.cs
  - id: openwiki-source-0638117c5f87f2a090f306ba
    resource: repo://BotFarm/Localization/AdminLocalizationApplicationExtensions.cs
  - id: openwiki-source-f74f3167832b85539e496d3b
    resource: repo://BotFarm/Localization/AdminLocalizationScope.cs
  - id: openwiki-source-c9db1e7afc6f7b7857513cc1
    resource: repo://BotFarm/Localization/CultureCookie.cs
  - id: openwiki-source-0254da0c12e084d0e555396c
    resource: repo://BotFarm/Localization/CultureCookieMiddleware.cs
  - id: openwiki-source-208d6cad096d6fbb90583433
    resource: repo://BotFarm/Localization/LocalizedIdentityErrorDescriber.cs
  - id: openwiki-source-eb904619a071eca6885b5c45
    resource: repo://BotFarm/Pages/Account/_LanguagePicker.cshtml
  - id: openwiki-source-7f2abf424b862fd6f7319dec
    resource: repo://TestBot/AdminLanguages/TestBot/en-US.json
  - id: openwiki-source-66cfed0bff7015b74aadf547
    resource: repo://TestBot/Pages/TestBotDashboard.razor
  - id: openwiki-source-2490216e45d5bee4b169d769
    resource: repo://tests/BotFarm.TestKit/AdminLocalizationTestExtensions.cs
generated: { by: "claude-code", at: "2026-11-12T10:04:59.900Z" }
---

# Admin UI Localization

The admin UI — the Blazor dashboard, each bot's page, and the Razor account pages — runs in a language the operator picks. It ships in English (`en-US`), Russian (`ru-RU`) and Ukrainian (`uk-UA`). This is separate from the bots' own chat texts, which `ILocalizationService` serves from each bot's `Languages/` folder (see [Adding a Bot Module](../workflows/adding-a-bot.md)).

## Translation sets

Strings live in flat JSON files at `AdminLanguages/<set>/<culture>.json`. A **set** is named after the assembly whose components use it:

| Set | Owner | Contents |
| --- | --- | --- |
| `BotFarm` | host project | account pages, the operational dashboard, the log viewer, Identity error texts |
| `BotFarm.Shared` | shared component library | layout, navigation, shared dashboard cards, `LanguageName`, MudBlazor built-in texts |
| `<BotName>` | each bot module | that bot's dashboard page texts (title, heading, logo alt text, footer) |

`JsonAdminStringLocalizerFactory` replaces the default `IStringLocalizerFactory`. It loads every set under `AdminLanguages` in the application base directory once, when it is created. Each file must be a JSON object with only string values; anything else throws `InvalidDataException`, so a broken file stops startup. Set and culture names compare ignoring case; keys compare exactly. `IStringLocalizer<T>` picks the set named after `T`'s assembly, so a component only needs `@inject IStringLocalizer<MyComponent> L`.

A key resolves to the current UI culture's value, then the `en-US` value, then the key itself marked as not found. Missing keys are logged at Debug level once per set, culture and key.

The host project copies `AdminLanguages/**/*.json` to its build and publish output. The shared library's and bot modules' sets end up in the same `AdminLanguages` folder of the host output, next to the host's own set.

## Supported languages

`AdminLanguageCatalog` (behind `IAdminLanguageCatalog`) derives the language list from the files in the `BotFarm.Shared` set. Each file name must be a predefined culture, and each file must define `LanguageName`, the language's name written in that language. `en-US` must exist; it is the default and is listed first, and the rest follow by culture name. To add a language, add a `<culture>.json` file to the `BotFarm.Shared` set and the host's `BotFarm` set; bot sets may add it too. The key-parity tests (see [Testing Strategy](../testing/strategy.md)) require an `en-US` file in every set, exactly the shared languages in the host set, and only shared languages in the other sets, so a bot set may cover just English and its page falls back to `en-US` in the other languages. Within a set, every file must have the same keys and no empty values.

`UseBotFarm` resolves the catalog before building the pipeline and logs the loaded languages, so a missing `en-US` file, a bad file name or a missing `LanguageName` fails at startup rather than on the first admin request.

## Picking the culture for a request

`AddAdminLocalization` configures `RequestLocalizationOptions` from the catalog: the supported cultures and UI cultures are the catalog's languages, the default is `en-US`, and the providers run in this order:

1. `CookieRequestCultureProvider` — the standard `.AspNetCore.Culture` cookie.
2. `AcceptLanguageMatchProvider` — the browser's `Accept-Language` header. Entries are tried by quality (header order on ties), skipping `*` and zero-quality entries. An entry matches a supported culture with the same name, or else the first supported culture with the same two-letter language, so `uk` or `ru-UA` still finds a supported culture.

If nothing matches, the request uses `en-US`.

`UseAdminLocalization` applies request localization only to admin UI paths. `AdminLocalizationScope` excludes `/api` (bot webhooks and the dashboard API), `/health` and `/healthz`. Those requests keep the server's default culture, so culture-sensitive bot code behaves the same whatever a caller sends, and they never get the language cookie. The middleware runs after routing and before authentication, so the sign-in and setup pages are localized too.

## Storing and switching the language

`CultureCookieMiddleware` runs in the same branch. For every `GET` request that accepts `text/html`, it writes the cookie with the culture the page was served in. This records the first-visit choice from `Accept-Language`, replaces an unsupported cookie value, and keeps the cookie alive for a year after the last visit. `CultureCookie.Append` writes `c=<culture>|uic=<culture>` with a one-year max age, path `/`, `HttpOnly`, `SameSite=Lax`, `IsEssential`, and `Secure` when the request came over HTTPS.

The switch endpoint is anonymous `GET /culture/set?culture=<name>&returnUrl=<local url>`. A supported culture (matched ignoring case) is stored in the cookie; any other value is ignored. It always redirects: to `returnUrl` when that is a local URL by the rules of `IUrlHelper.IsLocalUrl`, otherwise to `/`. This prevents open redirects.

Two pickers call it:

- `LanguagePicker` (in `BotFarm.Shared`, shown in the app bar by `MainLayout`) lists the catalog's languages, marks the current one, and navigates to the endpoint with `forceLoad: true`. A full page load is needed because a Blazor Server circuit keeps the culture it started with; the reload starts a new circuit in the chosen language.
- `_LanguagePicker.cshtml` is a row of plain links on the sign-in and setup pages, each returning to the current path and query.

## Third-party texts

- **MudBlazor**: `AdminMudLocalizer` is registered as MudBlazor's `MudLocalizer`. It reads MudBlazor's own keys (such as `MudTablePager_NextPage`) from the current culture's `BotFarm.Shared` file. It does **not** fall back to `en-US`, so a missing key shows MudBlazor's built-in English.
- **ASP.NET Core Identity**: `LocalizedIdentityErrorDescriber` translates the errors the admin password and user-name policy can produce (too short, needs a non-alphanumeric character, upper case, lower case or a digit, duplicate or invalid user name) using the `Identity.*` keys of the `BotFarm` set. Error codes stay Identity's own.
- **HTML encoding**: the host's `HtmlEncoder` allows Basic Latin, Latin-1 Supplement, General Punctuation and Cyrillic, so translated Razor pages are not turned into numeric entities.

## Containers and ICU

The Alpine runtime image normally runs in globalization-invariant mode, which supports no named culture. The `BotFarm/Dockerfile` base stage installs `icu-libs` and `icu-data-full` and sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`. Without them, `AdminLanguageCatalog` cannot create `ru-RU`/`uk-UA` and startup fails with a message pointing at invariant mode. See [Configuration and Operations](../operations/deployment.md).

## Bot modules

A bot module adds its own set at `<Bot>/AdminLanguages/<Bot>/<culture>.json`, with at least an `en-US` file plus any of the supported cultures it translates, and injects `IStringLocalizer<TheirPage>` in its dashboard page. The `templates/BotFarm.Bot` template and TestBot ship the `Page.Title`, `Page.Heading` and `Page.Footer` keys; the logo's alt text needs no key because the shared `BotDashboard` layout uses the bot name. Shared cards (`DashboardChats`, `DashboardStats`, `DashboardWebhook`, `DashboardBackups`) come already localized from `BotFarm.Shared`. See [Adding a Bot Module](../workflows/adding-a-bot.md).

## Testing

- `tests/BotFarm.Core.UnitTests/Localization` covers the factory (fallback, file validation, missing-key logging), the catalog, and `Accept-Language` matching.
- `tests/BotFarm.UnitTests/Localization` covers the cookie middleware, the switch endpoint and local-URL check, the pipeline scope (via `Microsoft.AspNetCore.TestHost`), the Identity describer, and key parity across every shipped set.
- `BotFarm.TestKit` has `AddAdminLocalizationForTests`, which registers the real localization over the shipped files in the test output; `CreateAdminLocalizer<T>()` for code built without a service provider; and `CultureScope`, which switches `CurrentCulture`/`CurrentUICulture` and restores them on dispose.

Related: [System Overview](./system-overview.md), [Operator Dashboard and Administration](../workflows/operator-dashboard.md).
