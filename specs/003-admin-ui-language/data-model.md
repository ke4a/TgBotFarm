# Data Model: Admin UI Language Selection

This feature stores no data in MongoDB. Its data is the translation files shipped with the application, an in-memory catalog built from them at startup, and one browser cookie.

## Admin translation file

One JSON file per language per translation set.

| Aspect | Rule |
|---|---|
| Location (output) | `AdminLanguages/<SetName>/<culture>.json` under the application base directory |
| Set name | Name of the assembly whose components use it (`BotFarm`, `BotFarm.Shared`, `TestBot`, …) |
| File name | A specific culture name, e.g. `en-US`, `ru-RU`, `uk-UA` |
| Content | A flat JSON object of string keys to string values; no nesting, no non-string values |
| Keys | `Area.Name` style, e.g. `Nav.Dashboard`, `Backups.Title`; MudBlazor keys keep their own names, e.g. `MudTablePager_NextPage` |
| Values | Non-empty; may contain `{0}`-style placeholders, formatted with the current culture |
| Required key | `LanguageName` in every file of the `BotFarm.Shared` set: the language's name written in that language |

**Validation** (startup and tests):
- Malformed JSON or a non-string value fails startup, naming the file.
- Every set has an `en-US` file.
- Within a set, every file has the same keys with non-empty values (FR-016).
- `BotFarm` has exactly the languages that `BotFarm.Shared` has. A bot set has no language outside that list, but may have fewer.

## Translation set (in memory)

Built by `JsonAdminStringLocalizerFactory` at startup.

| Field | Type | Notes |
|---|---|---|
| `Name` | string | Set name, compared ignoring case |
| `Languages` | dictionary of culture name → (dictionary of key → value) | Immutable after load |

**Lookup** for key *k* in set *S* under UI culture *c*: `S[c][k]`, then `S["en-US"][k]`, then *k* itself, marked not found. If *S* doesn't exist, the result is *k*, not found.

## Interface language (in memory)

Exposed by `IAdminLanguageCatalog`.

| Field | Type | Notes |
|---|---|---|
| `Culture` | `CultureInfo` | Specific culture, e.g. `uk-UA` |
| `DisplayName` | string | `LanguageName` value from that language's `BotFarm.Shared` file |

The catalog holds the languages of the `BotFarm.Shared` set, ordered with `en-US` first and the others by culture name. Its `Default` is `en-US`.

## Language preference (browser cookie)

| Attribute | Value |
|---|---|
| Name | `.AspNetCore.Culture` |
| Value | `c=<culture>\|uic=<culture>`, e.g. `c=ru-RU\|uic=ru-RU` |
| Max-Age | 365 days, renewed on every HTML document request and on every switch |
| Path | `/` |
| HttpOnly | yes |
| SameSite | Lax |
| Secure | when the request is HTTPS |
| Essential | yes (written even without a consent record) |

### How the preference changes

The preference is in one of four states:

| State | Meaning |
|---|---|
| Absent | No cookie |
| Valid | The cookie names a supported language |
| Invalid | The cookie is unsupported or malformed |
| Blocked | The browser rejects cookies |

These events move it between states:

| From | Event | To | Language shown |
|---|---|---|---|
| Absent | HTML document request | Valid | Best `Accept-Language` match, else `en-US` |
| Invalid | HTML document request | Valid (overwritten) | Best `Accept-Language` match, else `en-US` |
| Valid | HTML document request | Valid (expiry renewed) | Stored language |
| Any | `GET /culture/set` with a supported culture | Valid (new language) | Chosen language after the redirect |
| Any | `GET /culture/set` with an unsupported culture | Unchanged | Unchanged |
| Valid | One year with no document requests | Absent (expired by the browser) | Detection on the next visit |
| Blocked | Any request | Blocked | Detected each request; a switch lasts until the next reload |
