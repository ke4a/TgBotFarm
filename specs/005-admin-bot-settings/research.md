# Research: Admin-Managed Bot Settings

## Repository Findings

- Every reader of bot settings goes through named `IOptionsMonitor<BotConfig>`: `BotService`,
  `TelegramNotificationService` (admin chat), `WebhookSecretProvider` (webhook secret check and
  `SetWebhook`), `BotControlCoordinator` (initial enabled state) and `MainLayout` (emoji).
  `AddBotConfig(botName, configuration)` binds `Bots:{botName}:BotConfig` and calls
  `ValidateOnStart()`, so a missing or malformed token stops the application.
- `BotService` creates its `TelegramBotClient` in the constructor from the token and throws when the
  token is empty. Bot services are keyed singletons resolved by `BotControlCoordinator`'s
  constructor, so a missing token currently fails host startup.
- `Client` is read directly by update services, command and callback handlers, the notification and
  broadcast services, dashboard components (`DashboardChats`, `DashboardWebhook` through
  `GetWebhookInfoAsync`) and `TgUserAuthorizationHelper`.
- `BotControlCoordinator` already serializes all operations per bot with a lock, keeps a
  process-local `BotRuntimeStatus`, gates inbound updates through `CanProcessUpdates`, and stores
  desired enabled state in the `BotControlStates` collection of the `BotFarmControl` database. A
  missing record is seeded from `BotConfig.Enabled`.
- Data protection is registered by the host with keys persisted to `<data>/dpkeys` and the
  application name `BotFarm`. `BotFarm.Core` references the ASP.NET Core shared framework, so it can
  use `IDataProtectionProvider` without a new package.
- Per-bot backups dump only each bot's own database; `BotFarmControl` and `BotFarmIdentity` are not
  part of them.
- Bot API request URLs contain the token. Telegram.Bot builds its exception messages from fixed
  texts, Telegram's error description and the .NET network error, which names only the host and
  port, so its exceptions do not contain the URL. `IHttpClientFactory` request logging writes the
  full URL, so Telegram clients get their own `HttpClient`.
- `scripts/new-bot.sh`, `scripts/new-bot.ps1`, the bot template, `appsettings*.json` and the README
  all describe putting `Token`, `AdminChatId` and `Enabled` into configuration.

## Decisions

### Store-backed named options instead of a configuration source

**Decision**: Keep `IOptionsMonitor<BotConfig>` as the single way the application reads bot settings,
and fill it from a settings store registered in dependency injection: an
`IConfigureNamedOptions<BotConfig>` copies the store's decrypted in-memory snapshot into each named
`BotConfig`, and an `IOptionsChangeTokenSource<BotConfig>` per bot signals a change after every
accepted save or reload. The `Bots:*:BotConfig` configuration section is not read.

**Rationale**: Readers of `IOptionsMonitor<BotConfig>` keep working unchanged and see new values
after a save, which satisfies the configuration-pipeline constraint. A store registered in DI can
use the shared `IMongoClient`, the host's `IDataProtectionProvider`, logging and `TimeProvider`, can
load asynchronously during bot initialization, and can report per-bot states (not configured,
unreadable, store unavailable) that a configuration provider cannot express. It also makes the
stored settings the only source once they exist, without layering rules between providers.

**Alternatives considered**:

- A custom `IConfigurationProvider` added to `builder.Configuration`: rejected because it loads
  synchronously while the host is being built, before DI exists. It would need its own MongoDB client
  and data-protection provider, would block startup for the driver's server-selection timeout when
  MongoDB is down, cannot tell "no settings" from "store unavailable" or "unreadable", and would
  place decrypted secrets in `IConfiguration`, where configuration debug views can print them.
  Layering it with JSON and environment providers also lets values the database lacks show through
  from files, which conflicts with stored settings being the only source.
- Reading the store directly in every consumer: rejected because it changes all current readers and
  bypasses the options change notifications they already rely on.

### Storage location and shape

**Decision**: Store one document per registered bot in a `BotSettings` collection of the existing
`BotFarmControl` database. Tokens and webhook secrets are stored only as protected strings. The
document also keeps the token's numeric bot ID in plain form, with a unique sparse index on it. The
Telegram username is not stored: the dashboard shows it from the running bot's `Me`, and a save
that verifies a token returns the username Telegram reported. See [data-model.md](./data-model.md).

**Rationale**: Bot settings are platform control data, like enabled state, and must be readable
before a bot starts. Keeping them out of per-bot databases means bot backups never contain them and
a bot restore cannot change them. The bot ID is not secret (it is visible in every Telegram link to
the bot), lets the dashboard show which account a token belongs to without decrypting it, and lets
the database reject two registered bots using the same Telegram account even though encrypted
tokens cannot be compared.

**Alternatives considered**:

- Each bot's own database: rejected because backups would contain the secrets and a restore could
  roll settings back.
- `BotFarmIdentity`: rejected because it mixes bot operation data with operator accounts.
- Extending `BotControlStates` documents: rejected because desired state and settings change
  through different commands and have different write rules; separate documents keep both write
  paths simple.

### Encryption at rest

**Decision**: Protect each secret with `IDataProtector` created from the host's
`IDataProtectionProvider` with purposes `"BotFarm.BotSettings.v1"` and the bot name. Decrypt on load
into the in-memory snapshot only. A `CryptographicException` while unprotecting marks that bot's
settings unreadable ("Token must be re-entered") and leaves other bots unaffected.

**Rationale**: The existing key ring already protects sign-in cookies, needs no new configuration,
and handles key rotation: retired keys still decrypt older values. Including the bot name in the
purpose makes a protected value copied into another bot's document fail to decrypt. The key ring
lives in the data folder, not in MongoDB, so a database dump or a leaked `BotFarmControl` database
alone reveals no secrets.

**Alternatives considered**:

- A separate symmetric key in configuration: rejected because it reintroduces a secret into
  configuration and duplicates key management the framework already does.
- Hashing: rejected because the application must use the original token.
- Encrypting the key ring with a cloud key service: out of scope; the key ring stays as it is today.

### Startup order and the coordinator

**Decision**: `BotControlCoordinator` loads a bot's settings before its control state in the same
per-bot initialization pass and holds the bot's operation lock for both. For each bot it reads the
settings document, decrypts it into the snapshot,
signals the options change, and computes a settings state. Every bot whose settings document could
be read continues to the existing control-state load, which seeds a missing state disabled, so its
saved desired state is always known. Only a bot whose settings are complete and readable is then
applied. Other bots are reported with the new `BotRuntimeState.NotConfigured` (missing, incomplete
or unreadable settings) or stay `Unknown` (settings or control store unavailable), and stay gated.
`RetryAsync` reruns both loads.

**Rationale**: The coordinator already owns startup ordering, per-bot locking, degraded behavior and
explicit retry, so settings follow the same rules as enabled state without a second
orchestration path. Not configured bots keep their saved desired state; once valid settings are
saved, the coordinator applies that desired state as it would at startup.

**Alternatives considered**:

- Loading settings in a separate hosted service before the coordinator: rejected because it
  duplicates degraded-mode and retry handling and races with dashboard commands.

### Telegram client lifecycle

**Decision**: `BotService` stops creating its client in the constructor. It creates the client from
the current options value when settings first become usable and recreates it when the coordinator
reports a token change, under the bot's operation lock. While a bot has no usable token, reading
`Client` throws a `BotNotConfiguredException`. Operations an administrator starts for such a bot
(webhook info, dropping pending updates, sending a message to a chat, a broadcast) fail with that
exception before calling Telegram, and the dashboard shows a "not configured" message for it instead
of an error. Admin alerts for a bot with no usable token or admin chat are logged as a warning
instead of sent.

**Rationale**: Bot services are singletons resolved at startup, so the constructor can no longer
depend on a token that may not exist yet. Keeping `Client` as the access point leaves the many
existing call sites unchanged.

**Alternatives considered**:

- Recreating the whole keyed bot service: rejected because singletons are captured by other
  singletons and handlers.
- Restarting the application after a token change: rejected by the spec (FR-009).

### Saving settings

**Decision**: Saves go through a new `IBotSettingsService.SaveAsync`, implemented by
`BotControlCoordinator` the same way it implements `IBotControlService`, so it runs under the bot's
lock. The coordinator depends on a separate `BotSettingsProvider` for loading, the snapshot and
persistence, which keeps the two free of a circular dependency. A save is rejected while the bot is
`Unknown` or `Pending` when the change needs the bot to be re-applied (token or webhook secret on an
enabled bot). A bot in `Error` accepts a new token or webhook secret and has its desired on/off state
applied again with it, since a revoked token is a common reason for the failure and Retry alone would
reuse it. The flow is:

1. Validate the submitted values with the existing rules.
2. For a new token: verify it with `GetMe` through a temporary client from
   `ITelegramBotClientFactory`; reject when Telegram rejects it or cannot be reached. Reject when
   another registered bot already stores the same bot ID. When the bot ID differs from the stored
   one, require the caller's explicit confirmation.
3. Write the document (protected secrets, bot ID, `updatedAtUtc`, `updatedBy`).
4. Update the snapshot and signal the options change.
5. When the token or webhook secret changed and the bot is applied enabled: delete the webhook with
   the old client (a failure is logged and ignored, since a revoked token cannot do it), recreate
   the client, and re-apply the enabled state through the existing retry path. When disabled, only
   the client is recreated.

Admin chat and emoji changes need no re-apply and take effect through the options change.

**Rationale**: Reusing the coordinator lock keeps settings changes, enable/disable, maintenance
pauses and retries from interleaving. Verifying before writing keeps a working bot from being
switched to a token that cannot work. Deleting the old webhook first prevents the previous Telegram
account from continuing to deliver to this bot's endpoint after an account change.

**Alternatives considered**:

- Saving without verification and relying on the next enable to fail: rejected because the bot
  would already be running with the broken token.

### Error handling without leaking secrets

**Decision**: Verification and save failures are reported by category (`InvalidFormat`,
`RejectedByTelegram`, `TelegramUnreachable`, `DuplicateAccount`, `AccountChangeNotConfirmed`,
`StoreUnavailable`, `BotBusy`). Logs include the bot name, setting names, the category and the full
exception; `ApiRequestException.ErrorCode` is logged as a structured value.

The dashboard components that call Telegram directly (chats card and send message dialog) log the
exception with the error code and show a localized message instead of the exception text.

A unit test makes a real Telegram client fail the way Bot API calls fail (refused connection,
timeout, an HTML error page, malformed JSON, Telegram error answers) and checks that no resulting
exception contains the token. Another test checks that the Telegram client factory does not use
`IHttpClientFactory`.

**Rationale**: Full exceptions keep failures diagnosable, and Telegram client exceptions do not
contain the token. The tests catch a library or runtime change that would put the request URL into
an exception, and keep request URLs out of HTTP client logs. Categories are enough for operators
to act on and for the dashboard to show localized messages.

### No import from configuration

**Decision**: Do not read bot settings from configuration at all. A bot without a stored document is
`Incomplete` and shown as "Not configured" until an administrator saves its settings.

**Rationale**: The farm has few bots and one operator. Entering each token and admin chat once on the
dashboard after the upgrade costs a minute, while an import, its validation and warnings about values
left in configuration would add code and tests that run once.

### Starting disabled

**Decision**: Remove `Enabled` from `BotConfig`. A missing control-state record is seeded disabled.
Remove `Enabled` from `appsettings*.json`, the new-bot scripts, the template and the README.

**Rationale**: Required by the spec (FR-020) and the bot lifecycle spec: configuration no longer
decides whether a bot is enabled. Existing installations already have control-state records, so
their bots keep their state.

### Validation rules

**Decision**: Turn `BotConfigValidator` into a validator of submitted values with the
same token, admin chat and webhook secret rules plus an emoji limit, and remove its registration as
`IValidateOptions<BotConfig>` together with `ValidateOnStart()`. `AddBotConfig` keeps registering the
named options for the bot but no longer binds configuration; its configuration parameter is removed
and callers (bot modules, the template and new-bot scripts) are updated.

**Rationale**: Startup must not fail on missing settings (FR-010), but the token, admin chat and
webhook secret rules are unchanged (FR-005). The emoji limit counts text elements, so one emoji made
of several code points counts as one, and also caps the UTF-16 length, since a single text element
can carry any number of combining marks.

### Emoji picker

**Decision**: The dialog's emoji is chosen with `emoji-picker-element` (Apache-2.0), shown inline
under the emoji when the administrator clicks Choose, and can be cleared; it is never typed. The
library and its English emoji data (`emoji-picker-element-data`) are vendored in
`BotFarm.Shared/wwwroot/lib` and recorded in `BotFarm.Shared/libman.json`, and the picker's data
source points at the vendored file, so the admin page makes no CDN requests. The picker's colours
come from the MudBlazor palette variables, so it follows the app's light and dark themes. The limit
is one emoji; the server still validates it.

**Rationale**: Validating typed emoji is error-prone, and a picker gives search, categories and skin
tones without app code for them. The dependency is static files loaded only when the picker opens.
English data only: the library has no Ukrainian data, and one language keeps the vendored files
small.

### Dashboard

**Decision**: Add a settings card to each bot's dashboard page with an edit dialog. The card shows the
a warning icon in its header when the settings are not configured or could not be read, admin chat, emoji, bot ID, the Telegram username while the bot runs, whether a custom webhook secret is set,
and when and by whom settings were last changed. Token and webhook
secret inputs are plain text fields that start empty and that browsers and password managers are told
not to fill, correct or save; leaving them empty keeps the stored value, and
the webhook secret has a separate "use derived secret" action. An account change opens a
confirmation showing the old and new bot IDs. The bot enabled control shows "Not configured" for
missing, incomplete and unreadable settings alike, disables the switch and offers an "Open settings" button that opens the settings dialog
wherever the switch is shown; the card says "Token must be re-entered" when the settings are unreadable. All new text is added to the
en-US, ru-RU and uk-UA admin translations.

**Rationale**: Write-only inputs mean the server never renders a stored secret into the page
(FR-003). Placing the card on the bot's own page follows the existing card layout.

### Audit

**Decision**: Log each accepted and rejected change at information or warning level with
the bot name, changed setting names, the administrator's user name and the outcome category. Store
`updatedBy` and `updatedAtUtc` on the document for display.

**Rationale**: Satisfies FR-016 with the existing logging pipeline; a separate audit store is not
needed for a single-admin-cohort application.

### Testing

**Decision**: Unit-test the settings service, validation, options wiring, coordinator
integration, client recreation and dashboard components with NSubstitute substitutes for the store,
Telegram client factory and `IDataProtectionProvider` (`EphemeralDataProtectionProvider` where real
protection is needed). Add a `MongoBotSettingsStore` persistence test to the existing Testcontainers
integration project next to the control-state store tests, and extend host integration tests to
start with settings missing.

**Rationale**: Matches the constitution: routine tests use substitutes, and the persistence boundary
gets integration coverage like the existing control-state store.

### Compatibility and rollback

**Decision**: Rolling forward is the supported path; after the upgrade each bot's token and admin
chat are entered once on the dashboard. Rolling back to a version without this feature works only if
the settings are still, or again, present in configuration; the README says to keep them until the
upgrade is confirmed.

**Rationale**: The previous version reads only configuration and fails startup without a token.
