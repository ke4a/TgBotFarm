# Implementation Plan: Admin UI Language Selection

**Branch**: `master` (commits go directly to master) | **Date**: 2026-11-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-admin-ui-language/spec.md`

## Summary

The admin UI uses the standard ASP.NET Core localization pipeline.
- **Strings**: components and page models inject `IStringLocalizer<T>`. A custom `JsonAdminStringLocalizerFactory` serves the strings from `AdminLanguages/<AssemblyName>/<culture>.json` files. These use the same flat key–value format as the bot translation files but live in a separate tree. Each project owns its own set: host, shared UI, each bot.
- **Supported languages**: the languages of the shared UI set, with `en-US` as the default and fallback.
- **Culture per request**: `RequestLocalizationMiddleware` resolves it from the standard `.AspNetCore.Culture` cookie. A custom `Accept-Language` matcher handles first visits and matches the exact culture, then the base language.
- **Cookie**: a small middleware writes it on every HTML document request, which stores the detected language, replaces an invalid value, and renews the one-year expiry. An anonymous `GET /culture/set` endpoint switches the language and redirects back.
- **Picker**: in the Blazor layout it does a full reload, which starts a new circuit in the new culture. On the sign-in and setup pages it is a set of plain links.
- **Framework texts**: MudBlazor's texts go through a custom `MudLocalizer`. Identity's password errors go through a localized `IdentityErrorDescriber`. The Blazor reconnect dialog is replaced with a localized component.

## Technical Context

**Language/Version**: C# on .NET 10

**Primary Dependencies**:
- Existing: ASP.NET Core (Razor components with interactive server rendering, Razor Pages, `Microsoft.Extensions.Localization` from the shared framework), MudBlazor 9.12, AspNetCore.Identity.MongoDbCore.
- New: none.

**Storage**: JSON translation files shipped as content. One browser cookie. No database changes.

**Testing**: NUnit, NSubstitute and bUnit in `tests/BotFarm.Core.UnitTests`, `tests/BotFarm.UnitTests`, `tests/BotFarm.Shared.UnitTests` and `tests/TestBot.UnitTests`. HTTP behavior is tested with `DefaultHttpContext` and in-process pipeline tests; no live services.

**Target Platform**: Linux container (production); .NET development environments

**Project Type**: Multi-project ASP.NET Core application: host, shared core, shared UI, bot modules

**Performance Goals**: All translations are loaded once at startup, so a lookup is two dictionary reads. A switch costs one redirect and one full page load.

**Constraints**:
- A Blazor Server circuit's culture is fixed when the circuit starts, so switching requires a reload.
- Bot translation loading (`JsonLocalizationService`, `Languages/`) and its parity test must stay unchanged.
- Cookies set by page scripts are capped at 7 days in Safari, so the cookie must be set by the server.
- Browsers cap cookie lifetimes at 400 days.
- The app must not run in globalization-invariant mode, which supports no culture except the invariant one. The Alpine-based Docker image therefore installs ICU with full locale data (`icu-libs`, `icu-data-full`) and sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`.

**Scale/Scope**:
- 3 languages; 3 translation sets plus one per bot and the template.
- About 20 components and pages to convert: layout, home, dashboard, 6 shared cards and dialogs, log viewer, sign-in, setup, TestBot page, template page.

No NEEDS CLARIFICATION items remain. All decisions are in [research.md](./research.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Check | Pre-design | Post-design |
|---|---|---|---|
| I. Layered ownership | The reusable factory, catalog and `Accept-Language` matcher live in `BotFarm.Core`. The MudBlazor localizer and the picker live in `BotFarm.Shared`, which owns MudBlazor. The middleware, endpoint, Identity describer and host pages live in `BotFarm`. Each bot owns its admin translation set, found by assembly name, with no registration code. No new project references. | ✅ | ✅ |
| II. Secure configuration | No configuration or secrets. The cookie holds only a culture code and is `HttpOnly`, `SameSite=Lax`, and `Secure` on HTTPS. The switch endpoint validates `returnUrl` as local, which prevents an open redirect, and ignores unknown cultures. The endpoint is anonymous, but the worst a crafted link can do is change a visitor's display language. Authentication and authorization are unchanged. | ✅ | ✅ |
| III. Behavior-focused tests | New unit and pipeline tests for the factory, catalog, matcher, cookie middleware, endpoint, describer and file parity. Existing bUnit tests keep their English assertions through a TestKit registration helper. New render tests in `ru-RU`. No live services ([quickstart.md §1](./quickstart.md#1-automated-tests-routine-no-live-services)). | ✅ | ✅ |
| IV. Isolated persistent data | No persisted data. Bots' Telegram language settings are untouched, and bot webhook requests skip localization, so bots keep the server's default culture (FR-017). | ✅ | ✅ |
| V. Operable services | A malformed translation file fails startup with the file name, the same as bot translation files. Missing keys are logged at Debug level. Health checks, backups and webhooks are unaffected. Logged messages stay in English. | ✅ | ✅ |
| Workflow: rollback | Nothing is persisted server-side. Rolling back to the previous build ignores the cookie and shows English. | ✅ | ✅ |

Result: **PASS**, no violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/003-admin-ui-language/
├── plan.md                                  # This file
├── research.md                              # Phase 0 decisions R1–R12
├── data-model.md                            # Files, catalog, cookie and its state transitions
├── quickstart.md                            # Validation guide
├── contracts/
│   └── admin-localization-contracts.md      # HTTP, .NET and bot-author contracts
├── checklists/
│   └── requirements.md                      # Spec quality checklist
└── tasks.md                                 # Created by /speckit-tasks
```

### Source Code (repository root)

```text
BotFarm.Core/
├── Abstractions/IAdminLanguageCatalog.cs            # new: catalog + AdminLanguage record
├── Localization/JsonAdminStringLocalizerFactory.cs  # new: loads AdminLanguages/, creates localizers
├── Localization/JsonAdminStringLocalizer.cs         # new: culture → en-US → key lookup
├── Localization/AdminLanguageCatalog.cs             # new: languages of the BotFarm.Shared set
├── Localization/AcceptLanguageMatchProvider.cs      # new: exact, then base-language match
└── Extensions/ServiceCollectionExtensions.cs        # + AddAdminLocalization()

BotFarm.Shared/
├── AdminLanguages/BotFarm.Shared/{en-US,ru-RU,uk-UA}.json   # new: layout, cards, dialogs, Mud keys, units
├── Localization/AdminMudLocalizer.cs                # new: MudLocalizer over the shared set
├── Components/LanguagePicker.razor(.cs)             # new: app-bar menu, forceLoad navigation
├── MainLayout.razor(.cs)                            # picker, localized nav and aria labels
├── Components/*.razor(.cs)                          # localized texts, snackbars, pager labels
├── SharedResources.cs                               # new: marker type naming the BotFarm.Shared set
└── Utilities/FormatUtils.cs                         # current-culture numbers, localized units

BotFarm/
├── AdminLanguages/BotFarm/{en-US,ru-RU,uk-UA}.json  # new: host pages, router, reconnect dialog
├── BotFarm.csproj                                   # copy AdminLanguages/** to output
├── Localization/CultureCookieMiddleware.cs          # new: write/renew cookie on document GETs
├── Localization/AdminLocalizationScope.cs           # new: which paths get localization (not /api, /health, /healthz)
├── Localization/AdminLanguageEndpoints.cs           # new: GET /culture/set
├── Localization/LocalizedIdentityErrorDescriber.cs  # new
├── Components/ReconnectModal.razor                  # new: localized reconnect dialog
├── Extensions/WebApplicationExtensions.cs           # register localization, Mud localizer, describer; pipeline order
├── App.razor, Routes.razor                          # html lang, localized texts
├── Pages/Index.razor, Pages/Dashboard.razor(.cs)    # localized texts
├── Components/LogViewerDialog.razor(.cs)            # localized texts
└── Pages/Account/{Login,Setup}.cshtml(.cs), _LanguagePicker.cshtml   # localized texts, link picker, html lang

TestBot/
├── AdminLanguages/TestBot/{en-US,ru-RU,uk-UA}.json  # new
└── Pages/TestBotDashboard.razor                     # localized title, heading, footer

templates/BotFarm.Bot/BotTemplate/
├── AdminLanguages/BotTemplate/{en-US,ru-RU,uk-UA}.json   # new
└── Pages/BotTemplateDashboard.razor                 # localized

tests/
├── BotFarm.TestKit/AdminLocalizationTestExtensions.cs        # new: AddAdminLocalization for bUnit contexts
├── BotFarm.Core.UnitTests/Localization/                      # factory, catalog, matcher tests
├── BotFarm.UnitTests/Localization/                           # middleware, endpoint, describer, admin parity tests
├── BotFarm.Shared.UnitTests/Components/                      # register localization; ru-RU render tests
└── TestBot.UnitTests/                                        # only if TestBot page tests exist

README.md                                            # "9. Add translations": admin set; new "Admin UI languages" section
```

**Structure Decision**: Use the existing projects. The reusable mechanism goes in `BotFarm.Core` (it already references the ASP.NET Core shared framework). Code that depends on MudBlazor goes in `BotFarm.Shared`. The HTTP pipeline, Identity and host pages go in `BotFarm`. Translation sets sit next to the code that uses them.

## Middleware Order

```text
UseStaticFiles → UseRouting
→ UseAdminLocalization: UseWhen(not /api, /health, /healthz) { UseRequestLocalization, CultureCookieMiddleware }
→ UseAuthentication → UseAuthorization → UseAntiforgery → endpoints
```

Localization runs before authentication, so the sign-in redirect target and the sign-in page see the resolved culture. Static files return before the cookie middleware runs, so they never get a `Set-Cookie` header. Bot webhooks, the dashboard API and health endpoints skip the localization branch and keep the server's default culture (research R4a, FR-017).

## Implementation Order

1. **Core mechanism.** Factory, localizer, catalog, matcher, `AddAdminLocalization()`, and their tests.
2. **Host pipeline.** Cookie middleware, `/culture/set` endpoint, registration and pipeline order, the csproj content item, and their tests.
3. **Shared files and test kit.** Seed the `BotFarm.Shared` and `BotFarm` sets with `LanguageName` only. Add the TestKit helper. Register it in the existing bUnit tests, which still pass in English. Add the admin parity test.
4. **Shared UI.** `LanguagePicker`, `MainLayout`, every shared card and dialog, `FormatUtils`, `AdminMudLocalizer`. Fill in the ru/uk/en texts. Add the ru-RU render tests. After this step Stories 1–2 work on the dashboard.
5. **Host pages.** `App.razor`, `Routes.razor`, `Index`, `Dashboard`, `LogViewerDialog`, `ReconnectModal`, then `Login`/`Setup` with the link picker and the Identity describer (Story 3).
6. **Bots.** The TestBot set and page, and the template set and page (Story 4).
7. **Docs.** README.
8. **Validate.** Run all test projects, then the manual checks in [quickstart.md](./quickstart.md).

Steps 1 and 3 can proceed together. Step 2 needs step 1. Steps 4–6 need steps 1–3 and are independent of each other.

## Risks

| Risk | Mitigation |
|---|---|
| A Web SDK project may not copy its own `AdminLanguages/**/*.json` to the build or publish output by default. A missing set in the published image would stop the app at startup. | An explicit `Content Update` item with `CopyToOutputDirectory` and `CopyToPublishDirectory`. Quickstart §2 checks both the build and the `dotnet publish` output, and the parity test fails if the files are absent from the test output. |
| A missed hard-coded string leaves English in a translated page. | Review every `.razor` and `.razor.cs` file listed above. ru-RU render tests on the main cards. Quickstart §4 walks every page and dialog. |
| MudBlazor's set of localizer keys changes between versions. | Missing Mud keys fall back to MudBlazor's English. Visible pager texts are set through our own keys, not Mud's. |
| The `Accept: text/html` filter misses a document request, or adds the cookie to a non-document request. | Middleware tests cover browser navigations, `fetch` requests, `/_blazor` and static files. A missed renewal only delays renewal until the next document load. |
| Behind a TLS-terminating proxy, `Secure` isn't set. | Same as the existing sign-in cookie. Enabling forwarded headers is outside this feature (research R5). |
