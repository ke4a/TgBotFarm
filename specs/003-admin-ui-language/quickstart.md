# Quickstart: Validating Admin UI Language Selection

**Feature**: [spec.md](./spec.md) | **Contracts**: [contracts/admin-localization-contracts.md](./contracts/admin-localization-contracts.md) | **Data model**: [data-model.md](./data-model.md)

## Prerequisites

- .NET 10 SDK.
- For the manual checks: the app running locally (development authentication is fine) and a browser with developer tools.

## 1. Automated tests (routine; no live services)

```bash
dotnet test tests/BotFarm.Core.UnitTests
```

```bash
dotnet test tests/BotFarm.UnitTests
```

```bash
dotnet test tests/BotFarm.Shared.UnitTests
```

```bash
dotnet test tests/TestBot.UnitTests
```

Expected: all tests pass. The new tests cover:

| Area | Test class | Scenarios |
|---|---|---|
| String lookup | `JsonAdminStringLocalizerFactoryTests` | current culture value; `en-US` fallback for a missing key; key returned with `ResourceNotFound` when English lacks it too; set chosen by assembly name; unknown set; malformed file fails with the file name; placeholders formatted with the current culture |
| Catalog | `AdminLanguageCatalogTests` | languages from the shared set, `en-US` first; display names from `LanguageName`; startup fails without `en-US`; `Find` ignores case |
| Detection | `AcceptLanguageMatchProviderTests` | exact match; base-language match (`ru-BY` → `ru-RU`, `uk` → `uk-UA`); quality order (`de;q=1, ru;q=0.8` → `ru-RU`); `*` and unsupported only → no result; missing header → no result |
| Scope | `AdminLocalizationPipelineTests` | with a `ru-RU` cookie and `Accept-Language: uk-UA`, bot webhook, dashboard API, `/health` and `/healthz` requests run in the server's default culture and get no culture cookie; `/dashboard` and `/health-ui` run in `ru-RU`; `AdminLocalizationScope` path cases |
| Document language | `AppRenderTests` | `<html lang>` matches the current UI culture |
| Cookie | `CultureCookieMiddlewareTests` | first document request stores the detected language; invalid cookie replaced; a stored cookie wins over `Accept-Language`; valid cookie renewed with `Max-Age=31536000`, `HttpOnly`, `SameSite=Lax`, `Path=/`; `Secure` only on HTTPS; no header on non-HTML, non-GET or `/_blazor` requests |
| Switch | `CultureEndpointTests` | supported culture sets the cookie and redirects to `returnUrl`; unsupported culture leaves the cookie alone; external, protocol-relative and missing `returnUrl` redirect to `/`; works without authentication |
| Files | `AdminLanguageFileKeyParityTests` | every set has `en-US`; identical keys with non-empty values within each set; `BotFarm` languages equal `BotFarm.Shared` languages; bot sets use no other languages; every shared file has `LanguageName` |
| Identity texts | `LocalizedIdentityErrorDescriberTests` | each overridden error returns the current culture's text with its parameters |
| Components | existing render tests plus `MainLayoutTests`, `DashboardChatsRenderTests` | unchanged English output under `en-US`; in `ru-RU` the picker marks Русский, card titles are Russian, pager labels are Russian |

## 2. Manual check: build and publish output

Build the host:

```bash
dotnet build BotFarm
```

Confirm that `BotFarm/bin/Debug/net10.0/AdminLanguages/` contains `BotFarm/`, `BotFarm.Shared/` and `TestBot/`, each with `en-US.json`, `ru-RU.json` and `uk-UA.json`, and that `Languages/` still contains only `TestBot/`.

Publish the host the way the Docker image is built, replacing `<folder>` with an empty scratch folder:

```bash
dotnet publish BotFarm -c Release -o <folder>
```

Confirm that `<folder>/AdminLanguages/` has the same three sets and files. A missing set stops the app at startup.

## 3. Manual check: first visit (Story 2)

1. In a private window with the browser language set to Ukrainian, open the app. Expected: Ukrainian UI, and the dev tools show `.AspNetCore.Culture=c=uk-UA|uic=uk-UA` with a one-year expiry, `HttpOnly`, `SameSite=Lax`.
2. Repeat in a new private window with the browser languages set to German first, Russian second. Expected: Russian.
3. Repeat with German only. Expected: English, and `c=en-US|uic=en-US` is stored.
4. Edit the cookie value to `c=xx-XX|uic=xx-XX` and reload. Expected: the language is detected again and the cookie is overwritten.

## 4. Manual check: switching (Stories 1, 3, 4)

1. On the main dashboard, open the language menu. Expected: English, Русский, Українська, with the current one marked.
2. Choose Русский. Expected: the same page reloads in Russian: navigation, cards, dialogs (send message, log viewer), snackbars, the table pager, and month names in dates. Sizes show a decimal comma.
3. Open the TestBot page. Expected: the bot's own texts and its footer are in Russian.
4. Close the browser, reopen the app. Expected: still Russian, and the cookie's expiry has moved forward.
5. Sign out. Expected: the sign-in page is in Russian. Switch to Українська there, sign in with wrong credentials (error in Ukrainian), then sign in correctly. Expected: the dashboard is in Ukrainian.
6. Stop the app while a dashboard page is open. Expected: the reconnect dialog is in the chosen language.
7. Open `/culture/set?culture=ru-RU&returnUrl=https://example.com`. Expected: redirect to `/`, not off-site.

## 5. Manual check: no effect on bots (FR-017)

With the admin UI in Ukrainian, send a command to TestBot in a chat set to English. Expected: the bot answers in English.
