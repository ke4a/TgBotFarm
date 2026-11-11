---

description: "Task list for Admin UI Language Selection"
---

# Tasks: Admin UI Language Selection

**Input**: Design documents from `/specs/003-admin-ui-language/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/admin-localization-contracts.md](./contracts/admin-localization-contracts.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (III) requires behavior tests for every change, and FR-016 requires translation-file parity tests. Write each story's tests first and confirm they fail before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: The user story the task belongs to (US1–US4)

## Conventions used by every task

- **Translation files**:
  - Each is a flat JSON object of string keys to non-empty string values, UTF-8, two-space indented, in the format of `TestBot/Languages/TestBot/en-US.json`.
  - Keys use `Area.Name` style. For example: `Common.Cancel`, `Nav.Dashboard`, `Backups.Title`, `Chats.Delete.Confirm`.
  - MudBlazor keys keep MudBlazor's own names, such as `MudTablePager_NextPage`.
  - Placeholders use `{0}`.
- **Same keys everywhere**: when a task adds keys to a set, it adds them to that set's `en-US.json`, `ru-RU.json` and `uk-UA.json` together, with real Russian and Ukrainian translations.
- **Using the localizer**:
  - Components inject `IStringLocalizer<TheComponent>` as `L`: `@inject IStringLocalizer<X> L` in `.razor`, or `[Inject] private IStringLocalizer<X> L { get; set; } = default!;` in `.razor.cs`.
  - Code-behind uses `L["Key"]` or `L["Key", arg]`.
  - Converting a component means replacing every operator-visible literal with a key: text, `aria-label`, `title`, `Label`, `Placeholder`, `HelperText`, `<PageTitle>`, `SectionContent` text, `DataLabel`, snackbar messages, dialog titles and buttons, confirmation texts, and validation messages.
  - Bot names, chat titles, user names, message contents, log lines and bot translation values are left untranslated.
- **Doc comments**: describe purpose, not history.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the translation folders, make sure they reach the build and test outputs, and reference the localization namespace.

- [X] T001 [P] Create `BotFarm.Shared/AdminLanguages/BotFarm.Shared/en-US.json`, `ru-RU.json` and `uk-UA.json`. Each holds only `LanguageName` with the values `English`, `Русский` and `Українська`.
- [X] T002 [P] Create `BotFarm/AdminLanguages/BotFarm/en-US.json`, `ru-RU.json` and `uk-UA.json`, each with a single `App.Name` key whose value is `BotFarm` in all three.
- [X] T003 In `BotFarm/BotFarm.csproj`, add `<Content Update="AdminLanguages\**\*.json" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />`. In `BotFarm.Shared/BotFarm.Shared.csproj`, add the same item, but only if a build shows the files are not copied without it. Run `dotnet build BotFarm`. Confirm that `BotFarm/bin/Debug/net10.0/AdminLanguages/` contains both `BotFarm/` and `BotFarm.Shared/`, and that `Languages/` still contains only `TestBot/`. Then run `dotnet publish BotFarm -c Release -o <scratch folder>` and confirm the publish folder has `AdminLanguages/BotFarm/` and `AdminLanguages/BotFarm.Shared/` with all three files each. The Docker image is built from this output, and a missing set stops the app at startup.
- [X] T004 [P] Add `@using Microsoft.Extensions.Localization` to `BotFarm.Shared/_Imports.razor`, `BotFarm/_Imports.razor`, `TestBot/_Imports.razor` and `templates/BotFarm.Bot/BotTemplate/_Imports.razor`. Add `@using Microsoft.Extensions.Localization` to `BotFarm/Pages/Account/_ViewImports.cshtml` as well.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The JSON-backed localizer factory, the language catalog, request-culture resolution from the cookie for admin UI paths only, and the test helpers. Every story depends on these.

**⚠️ CRITICAL**: Finish this phase before starting any user story.

### Tests for the foundation

- [X] T005 [P] Write `tests/BotFarm.Core.UnitTests/Localization/JsonAdminStringLocalizerFactoryTests.cs` (NUnit). Build `AdminLanguages/<Set>/<culture>.json` trees in a `TempDirectoryScope` from `tests/BotFarm.TestKit`, and set `CultureInfo.CurrentUICulture` and `CurrentCulture` per test, restoring them in `TearDown`. Cover:
  - the value for the current UI culture;
  - the `en-US` fallback when the current culture lacks a key;
  - the key itself with `ResourceNotFound == true` when `en-US` lacks it too;
  - `Create(Type)` choosing the set named after `type.Assembly.GetName().Name`, ignoring case;
  - `Create(baseName, location)` choosing the set named `location`;
  - an unknown set returning the key with `ResourceNotFound`;
  - `L["Key", 1.5]` formatting with `CurrentCulture` (`1,5` under `ru-RU`);
  - `GetAllStrings(true)` merging missing `en-US` entries;
  - malformed JSON and non-string values throwing `InvalidDataException`, with a message that contains the file path;
  - a missing `AdminLanguages` root producing an empty factory, not an exception.
- [X] T006 [P] Write `tests/BotFarm.Core.UnitTests/Localization/AdminLanguageCatalogTests.cs`. Cover:
  - the languages come from the `BotFarm.Shared` set only, ordered `en-US` first and then by culture name;
  - `DisplayName` comes from each file's `LanguageName` key;
  - `Default` is `en-US`;
  - `Find("RU-ru")` returns the ru-RU entry;
  - `Find` returns null for null, empty, unknown and malformed culture names;
  - construction throws `InvalidOperationException` when the `BotFarm.Shared` set has no `en-US` file, or a file in it lacks `LanguageName`.
- [X] T007 [P] Write `tests/BotFarm.UnitTests/Localization/AdminLanguageFileKeyParityTests.cs`, modeled on `tests/BotFarm.UnitTests/Localization/LanguageFileKeyParityTests.cs` but reading `Path.Combine(AppContext.BaseDirectory, "AdminLanguages")`. Assert that:
  - the folder exists and contains the `BotFarm` and `BotFarm.Shared` sets;
  - every set has `en-US.json`;
  - within each set, all files have identical keys and non-empty values (one test case per set);
  - the `BotFarm` languages equal the `BotFarm.Shared` languages;
  - every other set uses only languages that `BotFarm.Shared` has;
  - every `BotFarm.Shared` file has `LanguageName`.

  Check that `tests/BotFarm.UnitTests/bin/Debug/net10.0/AdminLanguages/` is populated through the project reference. If it isn't, add a content link in `tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj`.
- [X] T008 [P] Write `tests/BotFarm.UnitTests/Localization/AdminLocalizationPipelineTests.cs` (FR-017). Build a `TestServer` pipeline with `AddAdminLocalization()` and `UseAdminLocalization()`, plus endpoints at `/api/TestBot/Update` (POST), `/api/dashboard/shutdown` (POST), `/health`, `/healthz`, `/health-ui` and `/dashboard` that each return `CultureInfo.CurrentCulture.Name` and `CurrentUICulture.Name`. Set `CultureInfo.DefaultThreadCurrentCulture` and `DefaultThreadCurrentUICulture` to `de-DE` for the test (restore them afterwards), so the server default is distinguishable from every supported language. Send every request with a `.AspNetCore.Culture=c=ru-RU|uic=ru-RU` cookie and `Accept-Language: uk-UA`. Assert that:
  - the `/api/*`, `/health` and `/healthz` requests run in `de-DE` and get no culture `Set-Cookie`;
  - `/dashboard` and `/health-ui` run in `ru-RU`.

  Also add `AdminLocalizationScope.Applies` cases: `/api`, `/api/x`, `/API/x`, `/health`, `/healthz` → false; `/`, `/dashboard`, `/health-ui`, `/apix`, `/Account/Login` → true.

### Implementation for the foundation

- [X] T009 [P] Create `BotFarm.Core/Abstractions/IAdminLanguageCatalog.cs` with `IAdminLanguageCatalog` (`Languages`, `Default`, `Find(string?)`) and `public sealed record AdminLanguage(CultureInfo Culture, string DisplayName)`, exactly as in contracts §3. Include XML doc comments.
- [X] T010 Create `BotFarm.Core/Localization/JsonAdminStringLocalizerFactory.cs`, a `public sealed` class implementing `IStringLocalizerFactory`. It:
  - takes an optional root path in its constructor, defaulting to `Path.Combine(AppContext.BaseDirectory, "AdminLanguages")`, and an `ILoggerFactory`;
  - loads every `<set>/<culture>.json` once into `FrozenDictionary<string, FrozenDictionary<string, FrozenDictionary<string, string>>>` (set → culture → key → value), with set and culture names compared ignoring case;
  - throws `InvalidDataException` naming the file for malformed JSON or non-string values;
  - exposes `IReadOnlyCollection<string> GetCultures(string set)` and `string? GetValue(string set, string culture, string key)` for the catalog and the MudBlazor localizer;
  - implements `Create(Type)` (set = assembly name) and `Create(string baseName, string location)` (set = `location`).

  It is public because `BotFarm.Shared` (the MudBlazor localizer) and the test projects use it directly.
- [X] T011 Create `BotFarm.Core/Localization/JsonAdminStringLocalizer.cs`, an `IStringLocalizer` bound to one set. Its indexers read `CultureInfo.CurrentUICulture.Name` at call time and look up the current culture, then `en-US`, then the key. For a missing key, they log once at Debug level per (set, culture, key) and return the key with `ResourceNotFound = true`. The indexer that takes arguments formats with `string.Format(CultureInfo.CurrentCulture, value, args)`. Also implement `GetAllStrings(bool includeParentCultures)`.
- [X] T012 Create `BotFarm.Core/Localization/AdminLanguageCatalog.cs`, an `internal sealed` implementation of `IAdminLanguageCatalog` built from the factory's `BotFarm.Shared` set. It validates `en-US` and `LanguageName` as T006 describes.
- [X] T013 Add `AddAdminLocalization(this IServiceCollection services, string? root = null)` in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`. It:
  - calls `services.AddLocalization()`;
  - registers `JsonAdminStringLocalizerFactory` (over `root`, or the default folder when null) as a singleton, both as itself and as `IStringLocalizerFactory`, replacing the default factory;
  - registers `IAdminLanguageCatalog` as a singleton;
  - configures `RequestLocalizationOptions`: `SupportedCultures` and `SupportedUICultures` from the catalog, `DefaultRequestCulture` set to `en-US`, and `RequestCultureProviders` holding only a `CookieRequestCultureProvider`. US2 adds the `Accept-Language` provider.

  Make T005 and T006 pass.
- [X] T014 In `BotFarm/Extensions/WebApplicationExtensions.cs`, call `services.AddAdminLocalization()` in `AddBotFarm`. Create `BotFarm/Localization/AdminLocalizationScope.cs` with `public static bool Applies(PathString path)`, which returns false when `path.StartsWithSegments` any of `/api`, `/health` or `/healthz` (ignoring case) and true otherwise. Add a `UseAdminLocalization(this IApplicationBuilder app)` extension in a new `BotFarm/Localization/AdminLocalizationApplicationExtensions.cs` that calls `app.UseWhen(context => AdminLocalizationScope.Applies(context.Request.Path), branch => branch.UseRequestLocalization())`, so bot webhooks, the dashboard API and health endpoints keep the server's default culture (research R4a). Call it in `UseBotFarm` between `UseRouting()` and `UseAuthentication()`. Update `tests/BotFarm.UnitTests/WebApplicationExtensionsTests.cs` if it asserts registrations or pipeline order. Make the FR-017 and scope cases of T008 pass.
- [X] T015 [P] Create `tests/BotFarm.TestKit/AdminLocalizationTestExtensions.cs` with two members:
  - `IServiceCollection AddAdminLocalizationForTests(this IServiceCollection services, string? root = null)`, which adds `NullLoggerFactory` logging and calls the core `AddAdminLocalization(root)`, so tests use the real factory and catalog over `AppContext.BaseDirectory/AdminLanguages` (or `root`);
  - a `CultureScope : IDisposable` helper (`using var _ = new CultureScope("ru-RU");`) that sets and restores both `CurrentCulture` and `CurrentUICulture`.

  Add the `BotFarm.TestKit` references this needs.
- [X] T016 Register `AddAdminLocalizationForTests()` and wrap each test in `CultureScope("en-US")` in every bUnit test context that renders an admin component. That covers, in `tests/BotFarm.Shared.UnitTests/Components/`, the `*RenderTests.cs` files, `BotEnabledControlTests.cs` and `MainLayoutTests.cs`, and in `tests/BotFarm.UnitTests/`, `Components/LogViewerDialogRenderTests.cs` and `Pages/DashboardBotControlRenderTests.cs`. Prefer a shared `SetUp` helper over copied lines. Run all four test projects; everything except T007 must pass.

**Checkpoint**: Localizers resolve from JSON, the catalog lists en/ru/uk, and admin UI requests take their culture from a valid `.AspNetCore.Culture` cookie, while bot webhooks, the dashboard API and health endpoints keep the server's default culture. The existing UI is still English, and all existing tests pass.

---

## Phase 3: User Story 1 - Operator switches the interface language (Priority: P1) 🎯 MVP

**Goal**: A language picker in the dashboard top bar switches the whole Blazor admin UI to English, Russian or Ukrainian. That includes MudBlazor's texts, dates and numbers. The choice persists in a server-set cookie.

**Independent Test**: Sign in, switch to each language from the picker, open every dashboard page and dialog, and confirm that all interface text and formatting follow the choice and survive a browser restart (quickstart §4, steps 1–4 and 6–7).

### Tests for User Story 1

- [X] T017 [P] [US1] Write `tests/BotFarm.UnitTests/Localization/CultureEndpointTests.cs`. Using an in-process `WebApplication` with `TestServer`, or `DefaultHttpContext` against the handler method, test `GET /culture/set`. Check that:
  - a supported culture (any letter case) writes `Set-Cookie: .AspNetCore.Culture=c%3Dru-RU%7Cuic%3Dru-RU` with `max-age=31536000`, `path=/`, `httponly` and `samesite=lax`, and `secure` only on HTTPS requests;
  - an unsupported or missing culture writes no cookie;
  - the response is always `302`;
  - `Location` equals a local `returnUrl` (`/dashboard?x=1`, `~/testbot/dashboard`);
  - `Location` is `/` for a missing, absolute (`https://example.com`), protocol-relative (`//example.com`) or backslash (`/\example.com`) `returnUrl`;
  - the endpoint works without authentication.
- [X] T018 [P] [US1] Write `tests/BotFarm.Shared.UnitTests/Localization/AdminMudLocalizerTests.cs`. Under `ru-RU`, a key that exists in the `BotFarm.Shared` ru file returns its value. A missing key returns `ResourceNotFound == true`, with no `en-US` fallback. Under `en-US` the localizer returns the en value.
- [X] T019 [P] [US1] Write `tests/BotFarm.Shared.UnitTests/Components/LanguagePickerTests.cs` (bUnit). It renders every catalog language by `DisplayName` and marks the current one under `CultureScope("uk-UA")`. Clicking `Русский` while on `http://localhost/testbot/dashboard?tab=1` navigates with `forceLoad: true` to `culture/set?culture=ru-RU&returnUrl=%2Ftestbot%2Fdashboard%3Ftab%3D1`. Assert through bUnit's `FakeNavigationManager` history.
- [X] T020 [P] [US1] Add `ru-RU` render tests in `tests/BotFarm.Shared.UnitTests/Components/` using `CultureScope("ru-RU")`:
  - `MainLayoutTests.cs`: nav labels, the theme toggle and sign-out `aria-label`s, and the picker are in Russian;
  - `DashboardChatsRenderTests.cs`: card title, column labels, `RowsPerPageString`/`InfoFormat` pager texts and the empty-state text are in Russian;
  - `DashboardBackupsRenderTests` (create it if absent): a backup date shows a Russian month abbreviation;
  - `tests/BotFarm.Shared.UnitTests/Utilities/FormatUtilsTests.cs`: `FormatBytes(1536, 1)` gives `1,5 КиБ` under `ru-RU` and `1.5 KiB` under `en-US`, and `null` gives the localized "N/A".

  Use the Russian values written in T027–T033.
- [X] T021 [P] [US1] Write `tests/BotFarm.UnitTests/Components/AppRenderTests.cs` (bUnit, FR-013). Render `App` with `Routes`, `HeadOutlet` and `ReconnectModal` stubbed through `ComponentFactories.AddStub<>()` and a substitute `IHttpContextAccessor`. Assert that the markup contains `<html lang="ru-RU">` under `CultureScope("ru-RU")` and `<html lang="en-US">` under `CultureScope("en-US")`.

### Implementation for User Story 1

- [X] T022 [US1] Create `BotFarm/Localization/AdminLanguageEndpoints.cs` with `MapAdminLanguageEndpoints(this IEndpointRouteBuilder endpoints)`. It maps `GET /culture/set` with `.AllowAnonymous()`, following contracts §1:
  - It resolves `culture` with `IAdminLanguageCatalog.Find`.
  - When a language is found, it appends the cookie with a shared `CultureCookie.Append(HttpResponse, HttpRequest, CultureInfo)` helper.
  - It returns `Results.Redirect` to the validated local `returnUrl`, or `/` otherwise. Validate with the same rules as `IUrlHelper.IsLocalUrl`: starts with `/` but not `//` or `/\`, or starts with `~/`, which maps to `/`.

  Put the helper in a new `BotFarm/Localization/CultureCookie.cs`. It uses `CookieRequestCultureProvider.DefaultCookieName` and `CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture))` with `MaxAge = TimeSpan.FromDays(365)`, `Path = "/"`, `HttpOnly = true`, `SameSite = SameSiteMode.Lax`, `Secure = request.IsHttps` and `IsEssential = true`. Call `app.MapAdminLanguageEndpoints()` in `UseBotFarm` in `BotFarm/Extensions/WebApplicationExtensions.cs` before `MapRazorComponents`. Make T017 pass.
- [X] T023 [P] [US1] Create `BotFarm.Shared/Localization/AdminMudLocalizer.cs`, an `internal sealed class AdminMudLocalizer : MudLocalizer`. Its constructor takes `JsonAdminStringLocalizerFactory`. It overrides `this[string key]` and `this[string key, params object[] arguments]` to return `GetValue("BotFarm.Shared", CurrentUICulture.Name, key)`, or `new LocalizedString(key, key, resourceNotFound: true)` when that's null. Register it in `AddBotFarm` in `BotFarm/Extensions/WebApplicationExtensions.cs` with `services.AddTransient<MudLocalizer, AdminMudLocalizer>()` after `AddMudServices`. If it is internal, add `InternalsVisibleTo` for `BotFarm` and `BotFarm.Shared.UnitTests` in `BotFarm.Shared/BotFarm.Shared.csproj`. Add these MudBlazor keys, used by the components in the admin UI, to the three `BotFarm.Shared` files: `MudTablePager_FirstPage`, `MudTablePager_PreviousPage`, `MudTablePager_NextPage`, `MudTablePager_LastPage`, `MudTable_Loading`, `MudDialog_Close`, `MudSnackbar_Close`, `MudAlert_Close` and `MudInput_Clear`. Make T018 pass.
- [X] T024 [US1] Create `BotFarm.Shared/Components/LanguagePicker.razor` and `LanguagePicker.razor.cs`. It is a `MudMenu` with a translate icon (`Icons.Material.Filled.Translate`), `Color.Inherit` and the `aria-label` `L["LanguagePicker.Label"]`, listing `IAdminLanguageCatalog.Languages`. Each item shows `DisplayName`, and the item whose culture equals `CultureInfo.CurrentUICulture.Name` is marked with a check icon and `aria-current="true"`. On click it calls `Navigation.NavigateTo($"culture/set?culture={Uri.EscapeDataString(name)}&returnUrl={Uri.EscapeDataString("/" + Navigation.ToBaseRelativePath(Navigation.Uri))}", forceLoad: true)`. Add the `LanguagePicker.Label` key. Make T019 pass.
- [X] T025 [US1] Update `BotFarm.Shared/MainLayout.razor` and `MainLayout.razor.cs`. Place `<LanguagePicker />` in the app bar before the theme toggle. Localize the nav-menu toggle `aria-label`, the theme toggle `aria-label`s, the sign-out `aria-label`/`title`, and the `Home`, `Dashboard`, `Health` and `Bots` labels. Use keys `Nav.*` and `AppBar.*` in the `BotFarm.Shared` set.
- [X] T026 [US1] Update `BotFarm.Shared/Utilities/FormatUtils.cs`. `FormatBytes(double? bytes, int decimals, IStringLocalizer units)` formats the number with `CultureInfo.CurrentCulture` and takes "N/A" and the unit labels from keys `Units.NotAvailable`, `Units.Bytes`, `Units.KiB` … `Units.YiB` (Russian and Ukrainian: `Б`, `КиБ`, `МиБ`, `ГиБ`, `ТиБ`, `ПиБ`, `ЭиБ`/`ЕіБ`, `ЗиБ`/`ЗіБ`, `ЙиБ`/`ЙіБ`). Update every caller (`DashboardStats.razor`, `DashboardBackups.razor`, `BotFarm/Pages/Dashboard.razor` and `Dashboard.razor.cs`) to pass an `IStringLocalizer<SharedResources>`. Create `BotFarm.Shared/SharedResources.cs`, an empty `public sealed class SharedResources` whose doc comment says it names the `BotFarm.Shared` translation set for code that isn't a component. A static class such as `FormatUtils` can't be a generic type argument. Update `tests/BotFarm.Shared.UnitTests/Utilities/FormatUtilsTests.cs` for the new signature.
- [X] T027 [P] [US1] Localize `BotFarm.Shared/Components/DashboardBackups.razor` and `DashboardBackups.razor.cs`: card title, buttons, table headers and `DataLabel`s, empty state, confirmations, restore and backup snackbars, and errors. Use keys `Backups.*` and `Common.*`. Keep the `dd MMM yyyy HH:mm:ss` date pattern.
- [X] T028 [P] [US1] Localize `BotFarm.Shared/Components/DashboardChats.razor` and `DashboardChats.razor.cs`: card title, search and filter labels, column headers and `DataLabel`s, row actions, the delete and clean-up confirmations, result snackbars and the empty state. Set the `MudTablePager` `RowsPerPageString` and `InfoFormat` from keys `Chats.Pager.RowsPerPage` and `Chats.Pager.Info`, where `Info` holds `{first_item}-{last_item} of {all_items}` with its tokens kept. Keys `Chats.*`.
- [X] T029 [P] [US1] Localize `BotFarm.Shared/Components/DashboardStats.razor` and `DashboardStats.razor.cs`, using keys `Stats.*`.
- [X] T030 [P] [US1] Localize `BotFarm.Shared/Components/DashboardWebhook.razor` and `DashboardWebhook.razor.cs`: health states, labels, the last-error caption, buttons and snackbars. Keys `Webhook.*`.
- [X] T031 [P] [US1] Localize `BotFarm.Shared/Components/BotEnabledControl.razor` and `BotEnabledControl.razor.cs`: switch label, tooltips, confirmation and snackbars. Keys `BotControl.*`.
- [X] T032 [P] [US1] Localize `BotFarm.Shared/Components/SendMessageDialog.razor` and `SendMessageDialog.razor.cs`: dialog title, field labels, helper and validation texts, buttons and result snackbars. Keys `SendMessage.*`. Localize the text in `SendMessageDialog.razor.js` only if it shows text to the operator; if so, pass the strings in from .NET.
- [X] T033 [P] [US1] Localize `BotFarm.Shared/Components/DashboardComponentBase.cs` if it builds any operator-visible message. Use shared error keys `Common.Error.*`, resolved through an `IStringLocalizer` that derived components supply.
- [X] T034 [US1] Localize the host's Blazor pages in the `BotFarm` set:
  - `BotFarm/Pages/Index.razor`: keys `Home.*`;
  - `BotFarm/Pages/Dashboard.razor` and `Dashboard.razor.cs`: page title, card titles, table headers, buttons, the shutdown dialog and snackbars, keys `Dashboard.*`;
  - `BotFarm/Components/LogViewerDialog.razor` and `LogViewerDialog.razor.cs`: title, level filter labels, live-tail toggle, buttons and the empty state, keys `Logs.*`. Log lines themselves stay untranslated.
- [X] T035 [US1] Update `BotFarm/App.razor` to `<html lang="@CultureInfo.CurrentUICulture.Name">` and add `@using System.Globalization`. Make T021 pass once T036 exists. Update `BotFarm/Routes.razor`: localize `<PageTitle>`, the not-found text (`Routes.NotFound`) and the footer (`Routes.Footer`). Keep `BotFarm` as the title section text. Keys go in the `BotFarm` set.
- [X] T036 [US1] Create `BotFarm/Components/ReconnectModal.razor`. It is a static (non-interactive) component rendering `<div id="components-reconnect-modal">` with the documented `components-reconnect-show`, `-failed` and `-rejected` CSS states. It shows localized texts for each state (`Reconnect.Rejoining`, `Reconnect.Failed`, `Reconnect.Retry`, `Reconnect.Rejected`, `Reconnect.Reload`), with a retry button calling `Blazor.reconnect()` and a reload button. Render it from `BotFarm/App.razor` before the Blazor script. Add minimal styles to `BotFarm/wwwroot/css/site.css` matching the dashboard theme.
- [X] T037 [US1] Run the four test projects. Fix every existing assertion broken by the new localizer parameters, such as constructors in `tests/BotFarm.Shared.UnitTests/Components/DashboardChatsTests.cs` and `SendMessageDialogTests.cs`, by passing the TestKit localizer, not by changing expected English texts. Run T007 and complete the ru/uk files until it passes.

**Checkpoint**: With a cookie set through the picker, the dashboard, every shared card and dialog, the log viewer, MudBlazor texts, dates, numbers and the reconnect dialog follow the chosen language. This is the MVP.

---

## Phase 4: User Story 2 - First visit uses the browser's language (Priority: P2)

**Goal**: Without a stored preference, the browser's best supported language is used and stored. The cookie is renewed on every document load, and an invalid value is replaced.

**Independent Test**: Quickstart §3: private windows with uk, de+ru, de-only and a tampered cookie.

### Tests for User Story 2

- [X] T038 [P] [US2] Write `tests/BotFarm.Core.UnitTests/Localization/AcceptLanguageMatchProviderTests.cs` against a provider built with en-US, ru-RU and uk-UA. Expected results:

  | `Accept-Language` | Result |
  |---|---|
  | `uk-UA` | `uk-UA` |
  | `UK-ua` | `uk-UA` |
  | `ru-BY` | `ru-RU` |
  | `uk` | `uk-UA` |
  | `en-GB` | `en-US` |
  | `de-DE,ru;q=0.8` | `ru-RU` |
  | `ru;q=0.5,uk;q=0.9` | `uk-UA` (quality order) |
  | `de,fr` | no result (`NullProviderCultureResult`) |
  | `*` | no result |
  | missing header | no result |
  | malformed header | no result, no exception |
  | five or more entries with the supported one last | still matched |
- [X] T039 [P] [US2] Write `tests/BotFarm.UnitTests/Localization/CultureCookieMiddlewareTests.cs`, using `DefaultHttpContext` with an `IRequestCultureFeature` set to the resolved culture, or a `TestServer` pipeline running `UseAdminLocalization`. Cover:
  - a `GET` with `Accept: text/html,...` and no cookie, where `Accept-Language` is `uk-UA`, stores `c=uk-UA|uic=uk-UA`;
  - with `de` only, it stores `en-US`;
  - a cookie `c=xx-XX|uic=xx-XX` is replaced with the detected culture;
  - a valid `ru-RU` cookie with `Accept-Language: uk-UA` keeps `ru-RU` (FR-007);
  - a valid `ru-RU` cookie is rewritten with `max-age=31536000`;
  - `POST`, `Accept: application/json`, `/_blazor/negotiate` and static-file requests get no `Set-Cookie` for the culture cookie;
  - `secure` appears only on HTTPS.

### Implementation for User Story 2

- [X] T040 [US2] Create `BotFarm.Core/Localization/AcceptLanguageMatchProvider.cs`, a `public sealed class AcceptLanguageMatchProvider : RequestCultureProvider`. Its constructor takes `IAdminLanguageCatalog`. It:
  - parses the header with `StringWithQualityHeaderValue.TryParseList`;
  - orders by quality (descending), keeping header order on ties, and skips `*` and zero quality;
  - for each entry, returns the first catalog culture with the same name (ignoring case), else the first with the same `TwoLetterISOLanguageName` (a `CultureNotFoundException` counts as no match);
  - returns `ProviderCultureResult` for a match, otherwise `NullProviderCultureResult`.

  In `AddAdminLocalization` in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`, set `RequestCultureProviders` to `[CookieRequestCultureProvider, AcceptLanguageMatchProvider]`. Build the options with `IConfigureOptions<RequestLocalizationOptions>` that resolves the catalog, so the provider gets the singleton. Make T038 pass.
- [X] T041 [US2] Create `BotFarm/Localization/CultureCookieMiddleware.cs`. For `GET` requests whose `Accept` header contains `text/html`, it reads `IRequestCultureFeature.RequestCulture.UICulture` and calls `CultureCookie.Append` from T022 before calling `next`. Register it in `UseAdminLocalization` in `BotFarm/Localization/AdminLocalizationApplicationExtensions.cs`, inside the same `UseWhen` branch, directly after `UseRequestLocalization()`. Confirm in `BotFarm/Extensions/WebApplicationExtensions.cs` that `UseStaticFiles` runs before it. Make T039 pass.

**Checkpoint**: A first visit picks the browser language or English and stores it. Every page load renews the cookie, and a tampered cookie is replaced.

---

## Phase 5: User Story 3 - Sign-in and setup pages are translated and offer the picker (Priority: P3)

**Goal**: The sign-in, setup and sign-out pages follow the chosen language, show its errors, and offer a link-based picker before sign-in.

**Independent Test**: Quickstart §4 step 5: signed out, switch on the sign-in page, fail and then succeed at signing in.

### Tests for User Story 3

- [X] T042 [P] [US3] Update `tests/BotFarm.UnitTests/Pages/Account/LoginModelTests.cs` and `SetupModelTests.cs` to pass an `IStringLocalizer<LoginModel>` or `IStringLocalizer<SetupModel>` from the TestKit factory. Keep the existing English expectations under `CultureScope("en-US")`, and add `ru-RU` cases for the "required", "invalid credentials", "locked out", "passwords do not match" and "invalid setup code" messages.
- [X] T043 [P] [US3] Write `tests/BotFarm.UnitTests/Localization/LocalizedIdentityErrorDescriberTests.cs`. Under `ru-RU`, each of `PasswordTooShort(8)`, `PasswordRequiresNonAlphanumeric()`, `PasswordRequiresUpper()`, `PasswordRequiresLower()`, `PasswordRequiresDigit()`, `DuplicateUserName("a")` and `InvalidUserName("a")` returns the Russian text with its argument and keeps the base class's `Code`. Under `en-US` they return English.

### Implementation for User Story 3

- [X] T044 [US3] Create `BotFarm/Localization/LocalizedIdentityErrorDescriber.cs` (`: IdentityErrorDescriber`). Its constructor takes `IStringLocalizer<LocalizedIdentityErrorDescriber>`, and it overrides the seven methods from T043 with keys `Identity.*` in the `BotFarm` set. Register it in `BotFarm/Extensions/WebApplicationExtensions.cs` by adding `.AddErrorDescriber<LocalizedIdentityErrorDescriber>()` to the `ConfigureMongoDbIdentity<ApplicationUser>(...)` chain. Make T043 pass.
- [X] T045 [US3] Update `BotFarm/Pages/Account/Login.cshtml.cs` and `Setup.cshtml.cs` to inject `IStringLocalizer<LoginModel>` or `IStringLocalizer<SetupModel>` and take every `ErrorMessage` from keys `Login.Error.*` and `Setup.Error.*`. In `SetupModel`, replace the `InvalidSetupCodeMessage` constant. Leave log messages in English. Make T042 pass.
- [X] T046 [US3] Create `BotFarm/Pages/Account/_LanguagePicker.cshtml`, a partial that injects `IAdminLanguageCatalog`. It renders a `<nav class="account-languages" aria-label="@L["LanguagePicker.Label"]">` of links. Each link points to `/culture/set?culture=<name>&returnUrl=<Request.Path + Request.QueryString, URL-encoded>` and shows `DisplayName`, with `aria-current="true"` on the current culture. Add styles to `BotFarm/wwwroot/css/account.css`.
- [X] T047 [US3] Update `BotFarm/Pages/Account/Login.cshtml`, `Setup.cshtml` and `Logout.cshtml`:
  - `<html lang="@CultureInfo.CurrentUICulture.Name">`;
  - inject `IStringLocalizer<LoginModel>`, `<SetupModel>` or `<LogoutModel>` and localize `<title>`, headings, the setup intro paragraph (keep `<code>Setup:Code</code>` markup by splitting the text into keys with `{0}` and using `Html.Raw` only around the fixed `<code>` element), labels and buttons;
  - render `<partial name="_LanguagePicker" />` inside `.account-card` on Login and Setup.

  Keys `Login.*`, `Setup.*`, `Logout.*` in the `BotFarm` set, with all three files updated.

**Checkpoint**: The sign-in, setup and sign-out pages and their errors follow the language, and switching before sign-in carries over to the dashboard.

---

## Phase 6: User Story 4 - Bot dashboard pages provide their own translations (Priority: P4)

**Goal**: Bot pages use translations shipped with the bot. Missing languages fall back to the bot's English text.

**Independent Test**: Under `uk-UA`, the TestBot page's own texts are in Ukrainian. With the bot's `uk-UA.json` temporarily removed, they show in English while the layout stays Ukrainian.

### Tests for User Story 4

- [X] T048 [P] [US4] Add a test in `tests/BotFarm.Core.UnitTests/Localization/JsonAdminStringLocalizerFactoryTests.cs`: a set with only `en-US.json` returns its English values under `uk-UA`, while the `BotFarm.Shared` set still returns Ukrainian. This covers the fallback case for a bot that lacks a language.
- [X] T049 [P] [US4] Write `tests/TestBot.UnitTests/Pages/TestBotDashboardTests.cs` (bUnit, adding `bunit` to `tests/TestBot.UnitTests/TestBot.UnitTests.csproj` if it isn't referenced). Render `TestBotDashboard` with the child cards substituted by `ComponentFactories` stubs. Assert that the page title, the screen-reader heading and the footer are the `TestBot` set's values under `ru-RU` and `en-US`. Add a check that `tests/TestBot.UnitTests/bin/.../AdminLanguages/TestBot/` exists.

### Implementation for User Story 4

- [X] T050 [P] [US4] Create `TestBot/AdminLanguages/TestBot/en-US.json`, `ru-RU.json` and `uk-UA.json` with keys `Page.Title` ("TestBot - BotFarm"), `Page.Heading` ("TestBot dashboard"), `Page.LogoAlt` and `Page.Footer` ("This is a test bot"), translated. Update `TestBot/Pages/TestBotDashboard.razor` to `@inject IStringLocalizer<TestBotDashboard> L` and use those keys. The `TitleSection` keeps `@Constants.Name`. Confirm the files appear under `BotFarm/bin/Debug/net10.0/AdminLanguages/TestBot/` after a build. If they don't, add the same `Content Update` item as T003 to `TestBot/TestBot.csproj`. Make T049 pass.
- [X] T051 [P] [US4] Create `templates/BotFarm.Bot/BotTemplate/AdminLanguages/BotTemplate/en-US.json`, `ru-RU.json` and `uk-UA.json` with the same four keys as T050, worded for a generic bot. Update `templates/BotFarm.Bot/BotTemplate/Pages/BotTemplateDashboard.razor` the same way. Check that the template's `.template.config/template.json` `sourceName` rename covers the `AdminLanguages/BotTemplate` folder name. If it doesn't, add a rename rule so a new bot gets `AdminLanguages/<NewBot>/`.

**Checkpoint**: All four stories work. Bot pages are localized from bot-owned files, with English fallback.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T052 [P] Update `README.md`:
  - extend "🤖 Creating a New Bot → 9. Add translations" with the admin translation set for the bot's dashboard page (location, same keys in every file, English fallback, no registration);
  - add an "Admin UI languages" section: how the language is chosen (picker, browser language on first visit, one-year cookie renewed on each visit) and how to add a language (a file in `BotFarm/AdminLanguages/BotFarm/` and `BotFarm.Shared/AdminLanguages/BotFarm.Shared/` with a `LanguageName` key, and optionally per bot).

  Put each command in its own code block.
- [X] T053 [P] Search for leftover operator-visible English literals in `BotFarm.Shared/`, `BotFarm/Pages/`, `BotFarm/Components/`, `BotFarm/App.razor`, `BotFarm/Routes.razor`, `TestBot/Pages/` and `templates/BotFarm.Bot/BotTemplate/Pages/`. Use `rg -n '>[A-Z][a-z]+[^<@]*<|aria-label="[A-Z]|title="[A-Z]|Label="[A-Z]|Placeholder="[A-Z]|Snackbar\.Add\("' --glob '*.razor' --glob '*.cs'`. Convert each hit, or confirm it isn't interface text (bot names, technical identifiers).
- [X] T054 Run all test projects one at a time, as in [quickstart.md §1](./quickstart.md#1-automated-tests-routine-no-live-services). Report any failure with its complete output.
- [X] T055 Run the manual checks in [quickstart.md §2–§5](./quickstart.md#2-manual-check-build-output) against the local app, using the browser preview. Record any check that couldn't be run.

---

## Dependencies & Execution Order

### Phase dependencies

| Phase | Depends on |
|---|---|
| Setup (T001–T004) | Nothing |
| Foundational (T005–T016) | Setup; blocks every story |
| US1 (T017–T037) | Foundational |
| US2 (T038–T041) | Foundational, plus `CultureCookie` from T022 for T041 |
| US3 (T042–T047) | Foundational, plus the `/culture/set` endpoint from T022 for the picker links in T046 |
| US4 (T048–T051) | Foundational only |
| Polish (T052–T055) | All stories |

US2's detection (T040) can be built on the foundation alone.

### Within the foundation

- T005–T008 come before T010–T014. T008's culture cases pass once T014 is done.
- T009, then T010, then T011, then T012, then T013, then T014.
- T015 comes before T016.

### Within US1

- T017–T021 first. T021 passes once T035 and T036 are done.
- T022, T023 and T024 can run in parallel.
- T025 needs T024.
- T026 comes before T027, T029 and T034, because they share the `FormatBytes` callers.
- T027–T033 are separate files, so they can run in parallel.
- T034–T036 come next, then T037.

### Parallel opportunities

| Phase | Tasks that can run together |
|---|---|
| Setup | T001, T002, T004 |
| Foundational | T005, T006, T007, T008 (tests); T009 and T015 |
| US1 | T017, T018, T019, T020, T021 (tests); T022, T023, T024; T027–T033 (one component each) |
| US2 | T038, T039 |
| US3 | T042, T043 |
| US4 | T048–T051 (US4 as a whole can also run alongside US1–US3 once the foundation is done) |
| Polish | T052, T053 |

### Parallel example: User Story 1 components

```text
T027 DashboardBackups   T028 DashboardChats   T029 DashboardStats
T030 DashboardWebhook   T031 BotEnabledControl T032 SendMessageDialog
```

Each edits its own `.razor`/`.razor.cs` pair. They all add keys to the same three `BotFarm.Shared` JSON files, so when running them in parallel, merge the JSON edits afterwards or give each its own key block.

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 and Phase 2, then run the tests: still English, all green.
2. Phase 3 (US1), then validate with quickstart §4 steps 1–4 and 6–7 by setting the language through the picker.
3. Stop and review. At this point the dashboard is fully switchable, but a first visit always shows English.

### Incremental delivery

1. Add US2: browser detection and cookie renewal (quickstart §3).
2. Add US3: sign-in and setup pages (quickstart §4 step 5).
3. Add US4: bot-owned page translations.
4. Polish: README, the leftover-literal sweep, and a full validation.

Each step leaves the app working and the tests green. Commit directly to `master` after each phase.
