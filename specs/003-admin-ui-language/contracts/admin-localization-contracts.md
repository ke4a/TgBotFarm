# Contracts: Admin UI Localization

These are the interfaces this feature adds. Some are for the browser (HTTP), some are for code inside the farm, and one is for bot authors (translation files). The [data model](../data-model.md) defines the file and cookie formats.

## 1. HTTP: switch language

```
GET /culture/set?culture={culture}&returnUrl={returnUrl}
```

| Parameter | Required | Rule |
|---|---|---|
| `culture` | yes | Culture name; matched against the supported languages ignoring case |
| `returnUrl` | no | Local, app-relative URL to return to |

- **Authentication**: none (`AllowAnonymous`), so it works from the sign-in and setup pages.
- **Effect**: if `culture` is supported, writes the language cookie with that culture and the attributes from the data model. Otherwise leaves the cookie unchanged.
- **Response**: always `302 Found`. `Location` is `returnUrl` when it is a local URL (starts with `/` but not `//` or `/\`, or starts with `~/`), and `/` otherwise.
- **Never**: returns an error page, redirects off-site, or echoes `culture` into the response.

## 2. HTTP: language cookie on document requests

For every `GET` request whose `Accept` header includes `text/html`, the response carries `Set-Cookie: .AspNetCore.Culture=c=<resolved>|uic=<resolved>` with `Max-Age=31536000`. Here `<resolved>` is the culture the request was served in, chosen in this order:
1. A valid cookie.
2. The best `Accept-Language` match: exact culture first, then the same base language, taken in quality order.
3. `en-US`.

Other requests (`/_blazor`, `/api/*`, bot webhooks, static files, `/health`, `/healthz`) don't get the header.

## 2a. HTTP: requests outside the admin UI

Requests under the `/api`, `/health` and `/healthz` path segments, including every bot webhook and the dashboard API, are not localized. They run in the server's default culture whatever `Accept-Language` header or culture cookie they carry, and never receive the culture cookie.

## 3. .NET: `IAdminLanguageCatalog` (BotFarm.Core)

```csharp
/// Supported admin UI languages, taken from the shared admin translation files.
public interface IAdminLanguageCatalog
{
    /// Supported languages, en-US first.
    IReadOnlyList<AdminLanguage> Languages { get; }

    /// The default and fallback language (en-US).
    AdminLanguage Default { get; }

    /// Returns the supported language whose culture name equals <paramref name="culture"/> ignoring case, or null.
    AdminLanguage? Find(string? culture);
}

public sealed record AdminLanguage(CultureInfo Culture, string DisplayName);
```

Registered as a singleton by `AddAdminLocalization()`. Used by the localization options, the switch endpoint, and both pickers.

## 4. .NET: string lookup (all admin UI code)

- Components and page models inject `IStringLocalizer<T>`, where `T` is any type in the component's own assembly. In practice that's the component itself.
- `localizer["Key"]` returns the value for the current UI culture, then the `en-US` value, then `"Key"` with `ResourceNotFound == true`.
- `localizer["Key", args]` formats the value with `CultureInfo.CurrentCulture`.
- `GetAllStrings(includeParentCultures)` returns the current culture's strings, plus the `en-US` entries missing from it when `includeParentCultures` is true.

Registration in the host:

```csharp
services.AddAdminLocalization();                        // Core: factory, catalog, RequestLocalizationOptions
services.AddTransient<MudLocalizer, AdminMudLocalizer>(); // Shared type, registered by the host
...
app.UseAdminLocalization();               // Host: for admin paths only, UseRequestLocalization + culture cookie middleware
app.MapAdminLanguageEndpoints();          // Host: GET /culture/set
```

## 5. Bot authors: admin translation files

A bot that adds a dashboard page:
1. Creates `AdminLanguages/<AssemblyName>/en-US.json` in the bot project. Adding `ru-RU.json` and `uk-UA.json` is optional but recommended. All files in the set must have the same keys.
2. Injects `IStringLocalizer<TheBotPage>` in its page and uses keys from those files.
3. Needs no registration code: the files reach the host's output through the project reference, and the factory finds the set by assembly name.

A language the bot doesn't ship falls back to the bot's `en-US` text. A bot set must not contain a language that the shared admin UI doesn't support (checked by tests).

## 6. MudBlazor

`AdminMudLocalizer : MudLocalizer` is registered as the application's `MudLocalizer`. For a Mud key it returns the current culture's value from the `BotFarm.Shared` set, or `ResourceNotFound`, which makes MudBlazor use its built-in English text.
