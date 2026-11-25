# Contracts: Metrics History and Charts

These are the interfaces this feature exposes to bots, to the host, to the admin UI and to the monitoring service. Signatures are illustrative C#; doc comments in the code state each contract.

## 1. `IUpdateService.Describe` and `CommandKeys` (BotFarm.Core, consumed by `UpdateControllerBase` and the host)

```csharp
UpdateDescription Describe(Update update) => /* default: by update type only */;
IReadOnlyCollection<string> CommandKeys => [];

public readonly record struct UpdateDescription(UpdateKind Kind, string? CommandKey);
public enum UpdateKind { Command, CallbackQuery, Other }
```

- Returns the update's kind and, for a command, its key:
  - `start` or `changelanguage` for the built-in commands;
  - the key of a registered command;
  - `Constants.Telemetry.UnknownCommand` (`(unknown)`) for any other command.
- Keys are lower case, without `/` and without `@botname`.
- Must not do I/O, throw, or change state.
- `UpdateService<TChatSettings>` overrides it using the same routing as `ProcessUpdate`. A command addressed to another bot is `Other`.
- **Default:**
  - a message starting with a bot command is `Command` with the unknown key;
  - a callback query is `CallbackQuery`;
  - anything else is `Other`.
- **`CommandKeys`**: the keys of the bot's built-in and registered commands, as `Describe` reports them. `UpdateService<TChatSettings>` returns `start`, `changelanguage` and its handlers' keys. The host uses them to start each command's count at zero.
- **Compatibility:** both are default interface members, so existing bots and the template compile unchanged.

## 2. `UpdateMetrics` (BotFarm.Core, singleton)

```csharp
public sealed class UpdateMetrics
{
    public const string MeterName = "BotFarm";
    public UpdateMetrics(IMeterFactory meterFactory);

    public void RecordProcessed(string botName, UpdateDescription description, TimeSpan duration);
    public void RecordOutcome(string botName, UpdateOutcome outcome);
    public void RecordZeros(string botName, IReadOnlyCollection<string> commandKeys); // every kind, outcome, key and (unknown), value 0
}

public enum UpdateOutcome { Failed, Retried, Dropped, Duplicate, Rejected }
```

- **Guarantees:**
  - no I/O;
  - never throws;
  - safe to call concurrently;
  - close to free when nothing listens to the meter.
- **Caller:** `UpdateControllerBase.Post`, which receives it as `[FromServices] UpdateMetrics`, so bot controller constructors are unchanged. The mapping from `Post` results to calls is the outcome table in [data-model.md](../data-model.md#outcomes-recorded-by-post).
- **Registration:** `AddCoreServices` registers it, so recording works whether or not export is on.

## 2b. `BotEventMetrics` (BotFarm.Core, singleton)

```csharp
public enum BotEvent
{
    BackupCreated, BackupFailed, BackupRestored, RestoreFailed,
    BotEnabled, BotDisabled, PendingUpdatesDropped, SettingsChanged
}

public sealed class BotEventMetrics
{
    public BotEventMetrics(IMeterFactory meterFactory);
    public void Record(string botName, BotEvent botEvent);
    public void RecordZeros(string botName);   // every event, value 0
}
```

- Same guarantees as `UpdateMetrics`. Counter `botfarm.bot.events` on meter `BotFarm`, tagged `bot` and `event`.
- **Callers:**
  - `MongoDbBackupService`: `BackupDatabase` records `BackupCreated` or `BackupFailed` from its result, and nothing when it is cancelled. `RestoreBackup` records `BackupRestored` or `RestoreFailed`.
  - `BotControlCoordinator`: `SetEnabledAsync` and `RetryAsync` record `BotEnabled` or `BotDisabled` when they leave the bot applied in a different on/off state than before. Applying the saved state at startup is not counted. `DropPendingUpdatesAsync` records `PendingUpdatesDropped` once Telegram confirms. `SaveAsync` records `SettingsChanged` once a save that changes at least one setting is stored.
- **Registration:** `AddCoreServices`. The telemetry startup service records its zeros for every registered bot.

## 3. Telemetry registration (BotFarm host)

```csharp
public static IServiceCollection AddBotFarmTelemetry(this IServiceCollection services, IConfiguration configuration);
```

- Called from `AddBotFarm`.
- **When no endpoint is set:** returns without registering anything. No `MeterProvider`, exporter or hosted service exists.
- **When an endpoint is set:**
  - registers OpenTelemetry metrics with the meters `BotFarm` and `System.Runtime`, and a resource with `service.name=BotFarm` and `service.version`, the host's informational version;
  - adds one `PeriodicExportingMetricReader` with a 60 s interval, with `LoggingMetricExporter`, an `OtlpMetricExporter` that logs failures once per outage, over HTTP/protobuf;
  - registers `AppMemoryMetrics`, the memory gauges, and a hosted service that creates them at startup and calls `RecordZeros` for every registered bot with that bot's `IUpdateService.CommandKeys`.
- **Endpoint:** `Telemetry:OtlpEndpoint` plus `/v1/metrics` if set. Otherwise the exporter's own handling of `OTEL_EXPORTER_OTLP_ENDPOINT`.
- **Headers:** `Telemetry:OtlpHeaders` if set, otherwise `OTEL_EXPORTER_OTLP_HEADERS`. Headers are never logged.
- **Export failures:** logged once per outage as a warning naming only the endpoint's host, and recovery is logged once. They never reach the webhook path.
- **Shutdown:** the meter provider is disposed with the host, which runs a final export.

## 4. `IMetricsLinks` (BotFarm.Core, consumed by BotFarm.Shared and host pages)

```csharp
public interface IMetricsLinks
{
    /// The monitoring dashboard filtered to botName, or null when no dashboard address is configured.
    string? ForBot(string botName);

    /// The monitoring dashboard's app memory panel, or null when no dashboard address is configured.
    string? ForAppMemory();
}
```

- `ForBot` adds `var-bot={botName}` to `Telemetry:DashboardUrl`.
- `ForAppMemory` adds `viewPanel=panel-{Constants.Telemetry.MemoryPanelId}`.
- Existing query parameters are kept.

## 5. UI contract

- **`MetricsLink`** (BotFarm.Shared): `<MetricsLink Href="…" />` renders an app bar icon button whose tooltip and accessible name are the translated "Metrics". It opens in a new tab with `rel="noopener noreferrer"` and renders nothing when `Href` is null.
- **`MainLayout`** has an `ActionsSection` outlet in the app bar, before the language picker, for a page's own actions.
- **`BotDashboard`** puts `<MetricsLink Href="@Links.ForBot(BotName)" />` in `ActionsSection`, so every bot gets it with no change and the cards keep their place.
- **`HealthDashboard`** puts `<MetricsLink Href="@Links.ForAppMemory()" />` in `ActionsSection` too.
- **Localization:** the key `Metrics.Link` ("Metrics") in `BotFarm.Shared/AdminLanguages/BotFarm.Shared/{en-US,ru-RU,uk-UA}.json`.

## 6. Monitoring-service contract

- **Data:** the stored metric names and labels in [data-model.md](../data-model.md) and research R10.
- **Dashboard:** `monitoring/grafana/dashboards/botfarm-dashboard.json`, uid `botfarm`. Its panels query only those names, and panel 3's deadline value equals `Constants.TelegramWebhookTimeout` in seconds. A unit test enforces both.
- **Local stack:** `monitoring/docker-compose.yml`. Grafana is on `http://localhost:3000` and OTLP/HTTP on `http://localhost:4318`, both bound to localhost only. The dashboard is provisioned from the repository, and no sign-in is needed.
