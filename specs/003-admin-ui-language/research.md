# Research: Admin UI Language Selection

Each entry records a decision, why it was made, and the alternatives that were rejected.

## R1. Localization mechanism

**Decision**: Use the standard `Microsoft.Extensions.Localization` abstractions. Razor components and Razor Pages inject `IStringLocalizer<T>`. A custom, public `JsonAdminStringLocalizerFactory` (an `IStringLocalizerFactory`, public because `BotFarm.Shared` and the tests use it directly) serves the strings from JSON translation files. `RequestLocalizationMiddleware` sets `CultureInfo.CurrentCulture` and `CurrentUICulture` for each request.

**Rationale**: Components keep the standard API, so the storage format can change later without touching them. Setting the culture through the middleware also makes date and number formatting, MudBlazor's built-in texts, and validation follow the chosen language (FR-011, FR-012). In Blazor Server the culture is captured from the request that opens the circuit, and Blazor restores it for every render on that circuit, so a cookie read by the middleware applies to the whole session.

**Alternatives considered**:
- Extending `ILocalizationService` (the bot translation service): it is keyed by bot name and language passed per call, so it can't follow the request culture and isn't visible to MudBlazor or Razor Pages.
- `.resx` resources: standard, but uses a different format from the bot translation files. The spec requires the same file format (FR-010).

## R2. Translation file layout

**Decision**: Admin translations live in a separate `AdminLanguages/<SetName>/<culture>.json` tree, one flat key–value object per language, the same format as `Languages/<BotName>/<culture>.json`. Each project owns its set and ships it as content:

| Project | Source path | Set name |
|---|---|---|
| `BotFarm` (host pages: sign-in, setup, home, main dashboard, log viewer, router) | `BotFarm/AdminLanguages/BotFarm/` | `BotFarm` |
| `BotFarm.Shared` (layout, shared dashboard cards and dialogs, MudBlazor texts) | `BotFarm.Shared/AdminLanguages/BotFarm.Shared/` | `BotFarm.Shared` |
| Each bot, e.g. `TestBot` | `TestBot/AdminLanguages/TestBot/` | `TestBot` |
| Bot template | `templates/BotFarm.Bot/BotTemplate/AdminLanguages/BotTemplate/` | `BotTemplate` (renamed with the bot) |

The set name is the name of the assembly that holds the component. Keys use a `Area.Name` style (for example `Backups.Title`, `Common.Cancel`).

**Rationale**: `JsonLocalizationService` and `LanguageFileKeyParityTests` treat every folder under `Languages/` as a bot, so admin files must not go there. A separate root keeps both systems unchanged (FR-010). One set per assembly gives each bot ownership of its own page texts (FR-015, constitution I) and makes the lookup rule mechanical. Bot `Languages/` folders already reach the host's output and the test outputs through project references, so the same delivery path works for `AdminLanguages/`. The host is a Web SDK project, so it gets an explicit `CopyToOutputDirectory` item for its own folder; this is verified in the quickstart.

**Alternatives considered**:
- A sub-folder under `Languages/` (for example `Languages/_Admin`): would be loaded as a bot by `JsonLocalizationService` and checked by the bot parity test.
- One file per component: many tiny files with no benefit at this size.
- Merging host and shared texts into one set: needs a special mapping rule from two assemblies to one folder.

## R3. Factory lookup and fallback

**Decision**: `JsonAdminStringLocalizerFactory` is a singleton that loads every set at startup into immutable dictionaries.
- `Create(Type)` picks the set named after `type.Assembly.GetName().Name`.
- `Create(baseName, location)` picks the set named `location` (the assembly name).
- Lookup reads `CultureInfo.CurrentUICulture.Name` at call time. The order is: the set's file for that culture, then its `en-US` file, then the key itself with `ResourceNotFound = true` (FR-014, spec edge cases).
- Formatted lookups (`localizer[key, args]`) format with `CurrentCulture`.
- A malformed JSON file fails startup with an exception naming the file, as bot translations already do.
- A missing key is logged at Debug level, once per set, culture and key.

**Rationale**: Reading the culture at call time works for both the request and the Blazor circuit. Loading once at startup matches the bot service and keeps lookups allocation-free.

**Alternatives considered**: Falling back to the parent culture (`ru` for `ru-BY`) inside the factory. That isn't needed, because the request culture is always one of the supported cultures (R4).

## R4. Supported languages and first-visit detection

**Decision**:
- `IAdminLanguageCatalog` lists the supported languages: the cultures that have a file in the `BotFarm.Shared` set. Startup fails if `en-US` is missing.
- Each language's display name is the `LanguageName` key of its own file (`English`, `Русский`, `Українська`), so the name appears in that language (spec Story 1, scenario 5).
- `RequestLocalizationOptions` uses these cultures, with `en-US` as the default.
- Providers, in order:
  1. `CookieRequestCultureProvider`
  2. A custom `AcceptLanguageMatchProvider`

  The query-string and default `Accept-Language` providers are removed.
- `AcceptLanguageMatchProvider` parses `Accept-Language` by quality value, ignores `*`, and returns the first entry that either:
  - equals a supported culture (ignoring case), or
  - has the same base language (`TwoLetterISOLanguageName`) as one.

**Rationale**:
- The built-in `AcceptLanguageHeaderRequestCultureProvider` with `FallBackToParentUICultures` maps `ru-BY` to `ru`, which isn't in the supported list (`ru-RU`). It also stops after three header entries by default. The custom provider covers FR-005 and Story 2 scenarios 2–3.
- `RequestLocalizationMiddleware` moves on to the next provider when a provider returns only unsupported cultures. A stale or malformed cookie therefore falls through to detection (FR-008).
- If neither provider matches, the default `en-US` is used (FR-006).

**Alternatives considered**:
- Listing supported cultures in `appsettings.json`: needs operator configuration, which the spec rules out.
- Adding neutral cultures (`ru`, `uk`, `en`) to the supported list: makes `CurrentCulture` a neutral culture, which formats dates and numbers inconsistently.

## R4a. Which requests are localized

**Decision**: The localization middleware and the cookie middleware (R5) run only for admin UI requests. They are wrapped in `app.UseWhen(...)` with the predicate `AdminLocalizationScope.Applies(PathString)`. It returns false for paths under the `/api`, `/health` and `/healthz` segments (matched with `PathString.StartsWithSegments`, so `/health-ui` is still included) and true for everything else. Bot webhooks (`/api/<Bot>/Update`) and the dashboard API (`/api/dashboard/*`) therefore keep the server's default culture.

**Rationale**: `RequestLocalizationMiddleware` sets `CurrentCulture` and `CurrentUICulture` for every request it sees. Telegram's webhook calls send no culture cookie, so with the middleware applied they would run in `en-US` instead of the server's default culture. Any culture-sensitive code in a bot (number and date formatting, casing, comparisons) could then change behavior, which FR-017 forbids. All controllers live under `/api`, so one prefix rule covers every current and future bot.

**Alternatives considered**:
- Applying localization everywhere and documenting the change: violates FR-017.
- Opting endpoints in through metadata: needs routing before localization and a marker on every page type, for no gain over the path rule.

## R5. Cookie format, lifetime and renewal

**Decision**:
- Use the standard `.AspNetCore.Culture` cookie, written with `CookieRequestCultureProvider.MakeCookieValue`. The value is `c=<code>|uic=<code>`, so it holds only the language code (FR-019).
- Options:

  | Option | Value |
  |---|---|
  | `MaxAge` | 365 days |
  | `Path` | `/` |
  | `HttpOnly` | true |
  | `SameSite` | Lax |
  | `Secure` | true when the request is HTTPS |
  | `IsEssential` | true |

- `CultureCookieMiddleware` runs after `UseRequestLocalization`, inside the same admin-only branch (R4a). On every `GET` whose `Accept` header includes `text/html` (document loads of Razor Pages, Razor components and the health UI, but not `/_blazor`, API, static file or webhook requests), it writes the cookie with the culture just resolved. That one step:
  - stores the detected language on the first visit (FR-005, FR-006),
  - replaces an invalid value (FR-008),
  - renews the one-year expiry on every visit (FR-004).
- The switch endpoint (R6) also writes the cookie.

**Rationale**:
- The server sets the cookie (FR-020), so Safari's 7-day limit on cookies set by page scripts doesn't apply.
- One year is within the 400-day cap browsers enforce, and the sliding renewal makes it last as long as the operator keeps visiting.
- The document-request filter keeps the header off high-volume requests.
- The host doesn't use forwarded headers, so behind a TLS-terminating proxy `IsHttps` is false and `Secure` isn't set. The Identity sign-in cookie behaves the same way, so this adds no new exposure. Turning on forwarded headers is outside this feature.

**Alternatives considered**:
- A custom cookie holding only `ru-RU`: needs a custom cookie provider as well, with no practical gain.
- Renewing only when the operator picks a language: the cookie would expire a year after the last choice even for daily users.

## R6. Switching the language

**Decision**:
- Add the minimal API endpoint `GET /culture/set?culture=<code>&returnUrl=<local path>`. It is anonymous, because it must work from the sign-in page.
- It writes the cookie when `culture` is a supported language and ignores the value otherwise. It then redirects to `returnUrl` if `IUrlHelper.IsLocalUrl`-style validation passes, and to `/` otherwise.
- In Blazor, the picker calls `NavigationManager.NavigateTo(..., forceLoad: true)` with the current relative URI as `returnUrl`.
- On the Razor Pages sign-in and setup pages, the picker is a partial with plain links.

**Rationale**:
- A full reload is the only way to change a Blazor Server circuit's culture.
- Validating `returnUrl` as a local URL prevents an open redirect (constitution II).
- An attacker could at most change a visitor's display language with a crafted link, so the endpoint uses GET without antiforgery. This keeps it a plain navigation from both Blazor and Razor Pages.

**Alternatives considered**:
- Setting `CultureInfo` on the circuit without reloading: the circuit's culture is fixed. Changing it mid-circuit leaves already-rendered components and MudBlazor state inconsistent.
- A POST endpoint with antiforgery: more ceremony on both page types, for no real security gain.

## R7. MudBlazor built-in texts

**Decision**:
- Register `AdminMudLocalizer : MudLocalizer` in `BotFarm.Shared`, as a transient `MudLocalizer` service.
- It looks up the key in the current culture's `BotFarm.Shared` file only. If the key isn't there, it returns `ResourceNotFound = true`, so MudBlazor falls back to its built-in English.
- The Mud keys used by the admin UI's components are stored under their exact Mud names in every language file: `MudTablePager_FirstPage`, `_PreviousPage`, `_NextPage`, `_LastPage`, `MudTable_Loading`, `MudDialog_Close`, `MudSnackbar_Close`, `MudAlert_Close` and `MudInput_Clear`.
- The visible pager texts are component parameters (`RowsPerPageString`, `InfoFormat`), not localizer keys, so the components set them from our own localizer.

**Rationale**: MudBlazor's `DefaultLocalizationInterceptor` uses its English resources for English cultures and calls the registered `MudLocalizer` for others, with English as a fallback. Overriding the localizer is MudBlazor's documented extension point.

**Alternatives considered**: Adding the `MudBlazor.Translations` package. It's a new runtime dependency for a handful of strings (constitution: additional constraints).

## R8. Dates, numbers and units

**Decision**:
- Date displays keep their `dd MMM yyyy HH:mm:ss` pattern, so month abbreviations follow the culture.
- `FormatUtils.FormatBytes` formats the number with `CurrentCulture` instead of `InvariantCulture`, so Russian and Ukrainian get a decimal comma.
- `FormatBytes` takes its unit labels and "N/A" from the `BotFarm.Shared` set through an `IStringLocalizer<SharedResources>` parameter. `SharedResources` is an empty marker class naming that set; the static `FormatUtils` class can't be a generic type argument.
- `<html lang>` uses `CurrentUICulture.Name`: in `App.razor`, `Login.cshtml` and `Setup.cshtml` (FR-013).

**Rationale**: This meets FR-012 with minimal layout change. A day-month-year pattern reads correctly in all three languages.

**Alternatives considered**: The culture's standard `G` format. It changes English output to month-first with AM/PM and makes table column widths vary by language.

## R9. Server-side messages on the sign-in and setup pages

**Decision**:
- `LoginModel` and `SetupModel` get their error messages from `IStringLocalizer<LoginModel>` and `IStringLocalizer<SetupModel>` (the `BotFarm` set).
- Identity's password and user-name errors on the setup page come from a `LocalizedIdentityErrorDescriber : IdentityErrorDescriber`, registered with `.AddErrorDescriber<>()`. It overrides only the errors the configured policy can produce: too short, requires non-alphanumeric, requires upper, requires lower, requires digit, duplicate user name, invalid user name.

**Rationale**: These are the only server-generated texts shown to operators (Story 3, scenarios 1 and 3). Logged messages stay in English (spec Assumptions).

## R10. Blazor reconnect UI and other framework texts

**Decision**: Replace the framework's default reconnect dialog with a `ReconnectModal` component in the host, rendered statically from `App.razor` with the `components-reconnect-modal` element ID. Its texts come from the `BotFarm` set. `App.razor`, `Routes.razor` (not-found text, footer) and `<PageTitle>` values are localized like other components.

**Rationale**: The built-in dialog's English text is hard-coded in `blazor.web.js`. The documented customization is a page-supplied element with that ID, rendered at the start of the request, when the culture is already known.

## R11. Tests

**Decision**:
- **`BotFarm.Core.UnitTests`:** add tests for the factory (lookup, English fallback, key fallback, set selection, malformed file) using `TempDirectoryScope`. Add tests for `AcceptLanguageMatchProvider` and the catalog.
- **`BotFarm.UnitTests`:**
  - Add tests for `CultureCookieMiddleware`: first visit, invalid cookie, a stored cookie winning over `Accept-Language`, renewal, non-document requests.
  - Add pipeline tests that a bot webhook or dashboard API request with `Accept-Language` and a culture cookie still runs in the server's default culture (FR-017), and that `<html lang>` matches the chosen language on the sign-in page (FR-013).
  - Add tests for the switch endpoint: valid and invalid culture, local and external `returnUrl`.
  - Add `AdminLanguageFileKeyParityTests`. Every set's files must have identical keys with non-empty values (FR-016), every set must have `en-US`, `BotFarm` must have exactly the languages `BotFarm.Shared` has, and no bot set may have a language outside that list.
  - Add tests for the localized Identity describer.
- **Component tests (bUnit):** a TestKit helper, `AddAdminLocalizationForTests()`, registers the real factory over the test output's `AdminLanguages` folder. Test setup pins `CurrentUICulture` to `en-US`, so existing English assertions stay valid. Add a few render tests in `ru-RU`: the layout picker, a dashboard card, and the pager labels.

**Rationale**: This follows constitution III: behavior-focused, no live services. The parity test is what makes SC-005 hold.

## R12. Bot authoring and documentation

**Decision**:
- `TestBotDashboard.razor` and the template's dashboard page use `IStringLocalizer<T>` with their own sets. The template ships `en-US`, `ru-RU` and `uk-UA` files.
- README's "Creating a New Bot" → "9. Add translations" step also covers admin translations for the bot's dashboard page.
- A new README section, "Admin UI languages", explains how to add a language: add a file to each core set, and optionally to each bot set.

**Rationale**: Covers FR-015 and keeps the bot template the reference for new bots.
