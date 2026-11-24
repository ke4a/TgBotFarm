# Data Model: Metrics History and Charts

The app stores no metrics. This file describes what it reports and the configuration that controls reporting. Retention and storage belong to the monitoring service.

## 1. Reported instruments

Meter `BotFarm`, created in BotFarm.Core (bot instruments) and the BotFarm host (memory gauges).

| Instrument | Type | Unit | Labels | Recorded when |
|---|---|---|---|---|
| `botfarm.updates` | Counter\<long> | `{update}` | `bot`, `kind` | A delivery is processed (FR-002) |
| `botfarm.update.outcomes` | Counter\<long> | `{delivery}` | `bot`, `outcome` | See the outcome table below (FR-004) |
| `botfarm.update.duration` | Histogram\<double> | `s` | `bot` | A delivery is processed, successfully or not (FR-003) |
| `botfarm.commands` | Counter\<long> | `{call}` | `bot`, `command` | A processed delivery is a command (FR-006) |
| `botfarm.memory.working_set` | ObservableGauge\<long> | `By` | none | Each collection (FR-007) |
| `botfarm.memory.gc_heap` | ObservableGauge\<long> | `By` | none | Each collection |
| `botfarm.memory.threshold` | ObservableGauge\<long> | `By` | none | Each collection |
| `botfarm.bot.events` | Counter\<long> | `{event}` | `bot`, `event` | A backup or restore finishes, an operator's on/off change is applied, or pending updates are dropped (FR-021) |

The meter `System.Runtime` is exported as well (FR-010). No other meters are.

**Resource**: `service.name=BotFarm`, plus `service.version`, the host's informational version.

### Label values

| Label | Values | Source |
|---|---|---|
| `bot` | Registered bot names | `IUpdateService.Name` |
| `kind` | `command`, `callback_query`, `other` | `IUpdateService.Describe` |
| `outcome` | `failed`, `retried`, `dropped`, `duplicate`, `rejected` | `UpdateControllerBase.Post` |
| `event` | `backup_created`, `backup_failed`, `backup_restored`, `restore_failed`, `bot_enabled`, `bot_disabled`, `pending_updates_dropped` | `MongoDbBackupService`, `BotControlCoordinator` |
| `command` | `start`, `changelanguage`, registered command keys, `(unknown)` | `IUpdateService.Describe`, lower case, no `@botname` |

No label holds chat, user or message data (FR-008).

### Outcomes recorded by `Post`

| Result of `Post` | `botfarm.update.outcomes` | Also recorded |
|---|---|---|
| Missing or invalid secret (401) | `rejected` | — |
| Bot not ready (503) | `rejected` | — |
| Already accepted (200, skipped) | `duplicate` | — |
| Processed successfully | — | updates, duration, commands |
| Temporary error, released for retry (503) | `failed`, `retried` | updates, duration, commands |
| Temporary error, last attempt (200) | `failed`, `dropped` | updates, duration, commands |
| Permanent error (200) | `failed` | updates, duration, commands |

Duration runs from just after the duplicate check until `ProcessUpdate` returns or throws.

### Histogram boundaries

0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60 seconds, set through `InstrumentAdvice<double>`.

### Startup zeros

When export is on, every registered bot gets `Add(0)` for each `kind` on `botfarm.updates`, each `outcome` on `botfarm.update.outcomes`, and each command key from `IUpdateService.CommandKeys` plus `(unknown)` on `botfarm.commands` (FR-009), and each `event` on `botfarm.bot.events` (FR-021).

## 2. Configuration

Section `Telemetry`, bound to `TelemetryOptions` in BotFarm.Core.

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Telemetry:OtlpEndpoint` | URL | empty | OTLP/HTTP base URL. The host appends `/v1/metrics`. Takes precedence over `OTEL_EXPORTER_OTLP_ENDPOINT`. |
| `Telemetry:OtlpHeaders` | string | empty | Export headers in OTLP form (`Authorization=Basic …`). **Secret**: set by environment variable or user secrets only. Takes precedence over `OTEL_EXPORTER_OTLP_HEADERS`. |
| `Telemetry:DashboardUrl` | URL | empty | Grafana dashboard address used for the admin "Metrics" links. Not secret. |

**Export is on** when `Telemetry:OtlpEndpoint` or `OTEL_EXPORTER_OTLP_ENDPOINT` is non-empty. Otherwise no OpenTelemetry services are registered.

**Validation at startup**: when set, `OtlpEndpoint` and `DashboardUrl` must be absolute `http` or `https` URLs. Otherwise the app refuses to start, as it does for other invalid settings.

### Shipped values

| File | `OtlpEndpoint` | `DashboardUrl` |
|---|---|---|
| `appsettings.json` | empty | empty |
| `appsettings.Development.json` | `http://localhost:4318` | `http://localhost:3000/d/botfarm/botfarm` |
| `docker-compose.override.yml` (development container) | `http://host.docker.internal:4318` | `http://localhost:3000/d/botfarm/botfarm` |
| `docker-compose.yml` (production container) | `${OTLP_ENDPOINT:-}` | `${GRAFANA_DASHBOARD_URL:-}` |

Production also passes `Telemetry__OtlpHeaders=${OTLP_HEADERS:-}`.

## 3. Dashboard definition

`monitoring/grafana/dashboards/botfarm-dashboard.json`, uid `botfarm`.

| Variable | Type | Query |
|---|---|---|
| `datasource` | datasource (Prometheus) | — |
| `bot` | query | `label_values(botfarm_updates_total, bot)` |

| # | Panel | Type | Unit | Series |
|---|---|---|---|---|
| 1 | Throughput | Time series, stacked bars, Min interval 2m | short | one per `kind` |
| 2 | Errors | Time series, lines, Min interval 2m | short | one per `outcome` |
| 3 | Handling time | Time series, lines | s | p50, p95, Webhook deadline (60, dashed) |
| 4 | Command usage | Bar gauge, sorted | short | one per `command` |
| 5 | App memory | Time series, lines | bytes (IEC) | Working set, GC heap, Threshold (dashed) |

The App memory panel has a fixed id, `Constants.Telemetry.MemoryPanelId`, which the health dashboard link uses.

| Annotation | Colour | Query |
|---|---|---|
| Backup created | green | `sum(botfarm_bot_events_total{bot="$bot", event="backup_created"} - botfarm_bot_events_total{bot="$bot", event="backup_created"} offset $__interval) > 0`, step 2m |
| Backup restored | blue | same, `event="backup_restored"` |
| Backup failed | red | same, `event="backup_failed"` |
| Restore failed | red | same, `event="restore_failed"` |
| Bot turned on | green | same, `event="bot_enabled"` |
| Bot turned off | orange | same, `event="bot_disabled"` |
| Pending updates dropped | yellow | same, `event="pending_updates_dropped"` |
| App restarted | purple | `count by (instance) (botfarm_memory_working_set_bytes) unless count by (instance) (botfarm_memory_working_set_bytes offset $__interval)`, step 2m; not filtered by bot |

## 4. Estimated series

5 bots with 10 registered commands each: about 240 bot series plus about 60 runtime series, under 300 in total (research R15).
