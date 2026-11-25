---
type: operations
title: Metrics and Observability
description: How BotFarm records per-bot update, command and lifecycle-event metrics plus app memory gauges, exports them over OTLP when an endpoint is configured, charts them in the provisioned Grafana dashboard, and links the admin UI to it.
tags: [operations, observability, metrics, opentelemetry, grafana]
sources:
  - id: openwiki-source-358534168ec187b0e4e08640
    resource: repo://BotFarm.Core/Telemetry/BotEventMetrics.cs
  - id: openwiki-source-9bb6cc228180a1ce5f363c2b
    resource: repo://BotFarm.Core/Telemetry/MetricsLinks.cs
  - id: openwiki-source-2da1f48ba99d1df7fdf741a5
    resource: repo://BotFarm.Core/Telemetry/TelemetryOptions.cs
  - id: openwiki-source-56a3ea93144976702ca78e94
    resource: repo://BotFarm.Core/Telemetry/UpdateMetrics.cs
  - id: openwiki-source-b4d82fa7a6133507fa36a6e9
    resource: repo://BotFarm.Shared/Components/BotDashboard.razor
  - id: openwiki-source-6e77e5a22412191ff62c000e
    resource: repo://BotFarm.Shared/Components/MetricsLink.razor
  - id: openwiki-source-9c5d9834a017d20667677a34
    resource: repo://BotFarm/appsettings.json
  - id: openwiki-source-0e49bf8c8f7c5dd7994da1bd
    resource: repo://BotFarm/Extensions/TelemetryExtensions.cs
  - id: openwiki-source-1ae7fd43f45774af7b690491
    resource: repo://BotFarm/Telemetry/AppMemoryMetrics.cs
  - id: openwiki-source-cbf96fede522a8ae309f5a51
    resource: repo://BotFarm/Telemetry/ExportFailureLog.cs
  - id: openwiki-source-9f9ebab53315c237cab76678
    resource: repo://BotFarm/Telemetry/LoggingMetricExporter.cs
  - id: openwiki-source-4a2fa543393c1b8146e24abf
    resource: repo://BotFarm/Telemetry/OtlpExportSettings.cs
  - id: openwiki-source-1f9658189dbad22032cf5ae1
    resource: repo://BotFarm/Telemetry/TelemetryStartupService.cs
  - id: openwiki-source-e1156789781fa335de5dbb66
    resource: repo://monitoring/docker-compose.yml
  - id: openwiki-source-1b141165fbb3ebaddba0b4fd
    resource: repo://monitoring/grafana/dashboards.yaml
  - id: openwiki-source-1ebda987297831b98b3b8714
    resource: repo://monitoring/grafana/dashboards/botfarm-dashboard.json
  - id: openwiki-source-4bfbd4fa3f322f7823f69219
    resource: repo://tests/BotFarm.IntegrationTests/Host/TelemetryExportTests.cs
  - id: openwiki-source-70e840ba1fcef5b7c52af756
    resource: repo://tests/BotFarm.UnitTests/Telemetry/DashboardDefinitionTests.cs
  - id: openwiki-source-94d5dc5377cfb917bc904df0
    resource: repo://tests/BotFarm.UnitTests/Telemetry/TelemetryRegistrationTests.cs
generated: { by: "claude-code", at: "2026-11-25T16:31:11.118Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-25T16:31:11.118Z
---

# Metrics and Observability

BotFarm measures each bot's webhook traffic and operator-visible events with .NET `System.Diagnostics.Metrics` instruments and, when an OTLP endpoint is configured, exports them with OpenTelemetry. A Grafana dashboard in `monitoring/` charts their history. Health checks (`/health`, the memory check) are a separate, point-in-time mechanism described in [Configuration and Operations](./deployment.md); the memory gauges here use the same sources so the chart matches the check.

## Ownership

| Piece | Location | Role |
| --- | --- | --- |
| `UpdateMetrics` | `BotFarm.Core/Telemetry` | Meter `BotFarm`: update throughput, delivery outcomes, handling time, command usage. Singleton from `AddCoreServices`. |
| `BotEventMetrics` | `BotFarm.Core/Telemetry` | Counter of backups, restores, on/off changes, dropped pending updates and settings changes on the same meter. Singleton from `AddCoreServices`. |
| `TelemetryOptions`, `IMetricsLinks` | `BotFarm.Core/Telemetry` | The `Telemetry` settings section (validated at startup) and the Grafana links built from it. |
| `AddBotFarmTelemetry` | `BotFarm/Extensions/TelemetryExtensions.cs` | Registers the OpenTelemetry meter provider, metric exporter, log exporter and startup service, only when export is on. |
| `AppMemoryMetrics`, `TelemetryStartupService`, `OtlpExportSettings`, `LoggingMetricExporter`, `ExportFailureLog` | `BotFarm/Telemetry` | Host-only memory gauges, zero-seeding at startup, exporter configuration and outage logging. |
| Local stack and dashboard | `monitoring/` | `grafana/otel-lgtm` container and the provisioned `botfarm` dashboard. |

The instruments are always registered, so recording works whether or not anything listens; without a listener the calls return almost immediately. Every recording method does no I/O, never throws and is thread-safe, and callers wrap recording so it can never change a webhook response or hide a processing error.

## What is recorded

All measurements carry only low-cardinality tags: the bot name and, depending on the instrument, the update kind, outcome, command key or event. No chat, user or message data is attached.

| Instrument (exported Prometheus name) | Type | Tags | Recorded by |
| --- | --- | --- | --- |
| `botfarm.updates` (`botfarm_updates_total`) | counter | `bot`, `kind` = `command`, `callback_query`, `other` | `UpdateControllerBase` for every processed delivery, success or failure |
| `botfarm.update.duration` (`botfarm_update_duration_seconds`) | histogram, seconds, buckets 0.01–60 s | `bot` | same; time from just after the duplicate check until handling ended |
| `botfarm.commands` (`botfarm_commands_total`) | counter | `bot`, `command` | same, for updates of kind `command` |
| `botfarm.update.outcomes` (`botfarm_update_outcomes_total`) | counter | `bot`, `outcome` = `failed`, `retried`, `dropped`, `duplicate`, `rejected` | `UpdateControllerBase` for deliveries that did not simply succeed |
| `botfarm.bot.events` | counter | `bot`, `event` = `backup_created`, `backup_failed`, `backup_restored`, `restore_failed`, `bot_enabled`, `bot_disabled`, `pending_updates_dropped`, `settings_changed` | `MongoDbBackupService`, `BotControlCoordinator` |
| `botfarm.memory.working_set`, `botfarm.memory.gc_heap`, `botfarm.memory.threshold` | observable gauges, bytes | none | `AppMemoryMetrics`, read at each collection |

The export also includes .NET's built-in `System.Runtime` meter (GC, thread pool, CPU, process memory).

How an update is classified comes from `IUpdateService.Describe`, which must do no I/O and never throw. `UpdateService<TChatSettings>` reuses its routing: a message is a command only when it is meant for this bot (so a group command addressed to another bot counts as `other`), and the key is lower case without `/` or `@botname`. A command with no built-in flow or registered handler is counted under `(unknown)` (`Constants.Telemetry.UnknownCommand`), which keeps the `command` tag bounded. See [Bot Runtime Lifecycle](../architecture/bot-runtime.md) and [Telegram Webhook Integration](../integrations/telegram-webhooks.md) for the delivery outcomes themselves.

A backup that is cancelled is not counted. On/off events are counted only when an operator's enable, disable or retry ends `Applied` in a different state than before, so maintenance pauses around backups do not appear as on/off changes. `settings_changed` is counted once per accepted settings save that changed at least one setting (see [Bot Settings](../architecture/bot-settings.md)).

## Export lifecycle

`AddBotFarm` calls `AddBotFarmTelemetry` after `AddCoreServices`. `OtlpExportSettings.Resolve` turns export on only when `Telemetry:OtlpEndpoint` or the standard `OTEL_EXPORTER_OTLP_ENDPOINT` is set; with neither, nothing OpenTelemetry-related is registered and no export warnings are logged. When on:

- The app's endpoint wins over the standard variable. `/v1/metrics` (metrics) and `/v1/logs` (logs) are appended to `Telemetry:OtlpEndpoint` (trailing slashes trimmed); with only the standard variable, the exporter's own handling applies. Headers come from `Telemetry:OtlpHeaders`, falling back to `OTEL_EXPORTER_OTLP_HEADERS`. `OtlpExportSettings.ToString` omits the headers because they hold a credential.
- Both exporters use OTLP HTTP/protobuf. Their HTTP client keeps idle connections for only 30 seconds, shorter than the export interval, so an export never reuses a connection the receiver has just closed.
- A `PeriodicExportingMetricReader` exports every 60 seconds and on host stop. The resource is `service.name=BotFarm` with `service.version` set to the host's informational version.
- Logs are exported in batches as they are written (`BatchLogRecordExportProcessor`), with the same resource. They follow the `Logging` configuration under the provider alias `OpenTelemetry`, so `Logging:OpenTelemetry:LogLevel` sets levels for the export alone (`appsettings.json` lowers `Microsoft.AspNetCore` to `Warning` there, keeping request logs out). Each record carries its formatted message and structured values. Log export failures are not logged, since those lines would go to the failing export too; a metric export outage reports the same endpoint.
- `LoggingMetricExporter` wraps the OTLP metric exporter and passes each result to `ExportFailureLog`, because the SDK reports failures only through its own event source. The log gets one warning when an outage starts and one information line when export recovers, naming only the endpoint's host, never its path, query or headers. The app keeps running while the receiver is down.
- `TelemetryStartupService` is registered after the OpenTelemetry services so the meter provider is already listening. At startup it creates the `AppMemoryMetrics` gauges and records zero for every registered bot (enabled or not): each event, each kind, each outcome, and each key in the bot's `IUpdateService.CommandKeys` plus `(unknown)`. Idle bots therefore report zero instead of no data, and the first update after startup is counted as an increase. A bot whose update service cannot be resolved is logged and skipped.

`TelemetryOptionsValidator` stops startup when `OtlpEndpoint` or `DashboardUrl` is set but is not an absolute `http` or `https` URL; its messages never include the headers.

## Grafana dashboard and links

`monitoring/grafana/dashboards/botfarm-dashboard.json` (uid `botfarm`) has `datasource` and `bot` variables and five panels in this order: Throughput (updates per interval by kind), Errors (outcomes by outcome), Handling time (p50 and p95 against the webhook deadline), Command usage (commands over the selected range, highest first), and App memory (working set and GC heap with the threshold, panel id 5). Annotations mark each bot event for the selected bot, including settings changes, and app restarts (a new instance starting to report). Counter panels and event markers subtract each series' value one step earlier (`x - x offset $__interval`) instead of using `increase()` over a step-long window, so every counter change is counted exactly once at any step size; the App memory queries take `max()` so each metric draws one line across restarts. `DashboardDefinitionTests` pin these properties, including that every query uses only exported metric names.

`IMetricsLinks` (implemented by `MetricsLinks`) builds admin links from `Telemetry:DashboardUrl`, read on every call so a changed setting applies without a restart. `ForBot` adds `var-bot=<name>`, `ForAppMemory` adds `viewPanel=panel-5`; existing query parameters are kept. With no dashboard URL both return `null` and the `MetricsLink` button is not rendered. Each bot's dashboard header (`BotDashboard`) shows the bot link, and the health page shows the app-memory link; see [Operator Dashboard and Administration](../workflows/operator-dashboard.md).

## Local stack and production

`monitoring/docker-compose.yml` runs `grafana/otel-lgtm` (OTLP receiver, metrics and log storage and Grafana in one container) with ports 3000 (Grafana), 4317 and 4318 (OTLP) bound to `127.0.0.1`, anonymous Grafana admin access (safe only because of that binding), the dashboard provisioned from the repository folder, and history kept in the fixed-name `botfarm-otel-lgtm-data` volume. The development `docker-compose.override.yml` includes it and points the app at it; `appsettings.Development.json` does the same for `dotnet run` (`http://localhost:4318`, dashboard `http://localhost:3000/d/botfarm/botfarm`). The stack can also be started on its own with `docker compose -f monitoring/docker-compose.yml up -d`.

In production, export is off by default. The production compose file maps `OTLP_ENDPOINT`, `OTLP_HEADERS` and `GRAFANA_DASHBOARD_URL` from `.env` to the `Telemetry__*` settings; the README walks through Grafana Cloud (its `/otlp` endpoint, a Basic `Authorization` header, importing the dashboard JSON). Keep the headers in the environment or a secrets store, never in tracked files. See [Configuration and Operations](./deployment.md).

## Focused tests

- `UpdateMetricsTests`, `BotEventMetricsTests`, `MetricsLinksTests` and `TelemetryOptionsTests` (Core) cover tags, zero-seeding, link building and URL validation.
- `TelemetryRegistrationTests`, `TelemetryStartupServiceTests`, `AppMemoryMetricsTests` and `ExportFailureLogTests` (host) cover export on/off and endpoint precedence, startup zeros, the gauges, and one-warning-per-outage logging. `DashboardDefinitionTests` and `LocalStackDefinitionTests` check the dashboard JSON and the compose stack.
- `UpdateControllerTests` cover recording for each delivery path; `TelemetryExportTests` and `WebhookPipelineTests` in `BotFarm.IntegrationTests` check metrics end to end through the in-memory host. `TelemetryExportTests` send real exports to a fake OTLP receiver (`FakeOtlpEndpoint`): metrics path and headers, a final export on stop, one warning for repeated failures, app logs on the logs path without ASP.NET Core request logs, and neither a meter provider nor log export without an endpoint. `BotFarm.TestKit`'s `TestMetrics` supplies isolated metric services for tests that do not check what is recorded. See [Testing Strategy](../testing/strategy.md).
