# Implementation Plan: Metrics History and Charts

**Branch**: `[master]` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-metrics-history-charts/spec.md`

## Summary

Measure each bot's updates in `UpdateControllerBase.Post`, plus app memory, with .NET's built-in `System.Diagnostics.Metrics`. Export them over OTLP with the OpenTelemetry SDK, but only when an endpoint is configured. Grafana Cloud's free plan stores and charts them in production, and a local `grafana/otel-lgtm` container does so in development.

The app stores no metrics. A Grafana dashboard committed to the repository provides the charts, and the admin dashboards link to it. With no endpoint set, no OpenTelemetry services are registered, and the measurement calls do almost nothing because no listener is attached. Research decisions are in [research.md](research.md).

## Technical Context

**Language/Version**: C# on .NET 10 (`net10.0`)

**Primary Dependencies**:
- Existing: ASP.NET Core and Blazor Server, MudBlazor 9.14, Telegram.Bot 22.10.
- New, in the BotFarm host only: `OpenTelemetry.Extensions.Hosting` 1.21.0 and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.21.0.
- New, test projects only: `Microsoft.Extensions.Diagnostics.Testing` 11.0.1.

**Storage**: None. Metrics live in the monitoring service, which also sets retention (14 days on the free plan).

**Testing**:
- NUnit 5 with NSubstitute for unit tests.
- `MetricCollector<T>` to read measurements.
- bUnit for the link component.
- A fake OTLP endpoint (local Kestrel) in `BotFarm.IntegrationTests` for the export path.
- No test needs Grafana, Telegram or the network.

**Target Platform**: Linux container (non-root) and local development on macOS or Windows. The local monitoring stack runs in Docker.

**Project Type**: Web service with a Blazor admin UI, plus bot plugin projects.

**Performance Goals**:
- With export off, webhook response times are unchanged (SC-008).
- With export on, recording costs the SDK's normal in-memory aggregation, about 100 ns per measurement, with no I/O on the webhook path.

**Constraints**:
- With export off, no exporter, reader or timer runs (FR-011).
- Credentials only through environment variables or user secrets (FR-013).
- Fewer than 2,000 series for 5 bots (SC-003).

**Scale/Scope**:
- A handful of bots.
- 4 bot instruments, 3 memory gauges, the runtime meter.
- 1 dashboard with 5 panels, 2 admin links in 3 languages, and 1 local compose file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| I. Layered ownership | Classification, instruments, options and link building live in BotFarm.Core. The OpenTelemetry SDK, exporter and memory gauges live in the BotFarm host. The link component lives in BotFarm.Shared. Bots change nothing. No new project references. | Pass |
| II. Secure configuration and access | The OTLP headers (the Grafana Cloud token) come only from environment variables or user secrets and are never logged; export failures log only the endpoint's host. Metrics hold no chat or user data. The local Grafana allows anonymous Admin access, but it is a development-only container bound to `127.0.0.1`, and production uses Grafana Cloud's own accounts. | Pass |
| III. Behavior-focused tests | Tests cover classification, controller outcomes, registration on and off, endpoint precedence, failure logging, memory gauges, links and the dashboard file. The export path is tested against a fake local endpoint, never live Grafana. | Pass |
| IV. Isolate and protect persistent data | Nothing is persisted. Backups, restores and per-bot databases are untouched. | Pass |
| V. Operable and recoverable services | Export failures are logged once per outage and recovery once. The meter provider follows the host lifecycle and flushes on shutdown. Health checks are unchanged. Losing the monitoring service never affects bots. | Pass |
| Additional constraint: runtime dependencies | Two OpenTelemetry packages, in the host only. They meet a demonstrated need: history and charts without custom storage. Operational cost: one HTTPS request a minute, a few KB, when export is on, and nothing when it is off. | Pass |

**Post-design re-check (after Phase 1)**: Still passing. The contracts add:
- two default interface members to `IUpdateService`;
- one `[FromServices]` parameter to `UpdateControllerBase.Post`;
- one `Telemetry` configuration section.

All are source-compatible for existing bots and the template.

## Compatibility and Rollback

- **Bots and template**: No required changes. `IUpdateService.Describe` and `IUpdateService.CommandKeys` have default implementations, and `UpdateControllerBase.Post` receives `UpdateMetrics` through `[FromServices]`.
- **Configuration**: The new `Telemetry` section is optional. Existing deployments keep working with export off.
- **Data**: None stored, so there is nothing to migrate or roll back.
- **Rollback**: Revert the code. Then remove the `Telemetry__*` settings and, if wanted, the dashboard and data in Grafana Cloud.

## Project Structure

### Documentation (this feature)

```text
specs/004-metrics-history-charts/
├── plan.md              # This file
├── research.md          # Phase 0 decisions
├── data-model.md        # Instruments, labels, configuration, dashboard
├── quickstart.md        # Validation guide
├── contracts/
│   └── metrics-contracts.md   # Describe, UpdateMetrics, registration, links, UI, monitoring service
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
BotFarm.Core/
├── Abstractions/
│   ├── IUpdateService.cs              # + Describe(Update), CommandKeys default members
│   └── IMetricsLinks.cs               # new: admin links to the monitoring dashboard
├── Base/UpdateService.cs              # override Describe and CommandKeys with the existing routing
├── Controllers/UpdateControllerBase.cs # record outcomes and duration in Post
├── Constants.cs                       # + TelegramWebhookTimeout, Telemetry constants
├── Services/TelegramBotClientFactory.cs # use Constants.TelegramWebhookTimeout
├── Telemetry/                         # new folder
│   ├── UpdateMetrics.cs               # meter "BotFarm", bot instruments
│   ├── UpdateDescription.cs           # UpdateKind, UpdateDescription, UpdateOutcome
│   ├── TelemetryOptions.cs            # Telemetry section, validation
│   └── MetricsLinks.cs                # IMetricsLinks implementation
└── Extensions/ServiceCollectionExtensions.cs # register UpdateMetrics, options, links

BotFarm/
├── BotFarm.csproj                     # + OpenTelemetry packages
├── Extensions/TelemetryExtensions.cs  # AddBotFarmTelemetry: on/off, precedence, reader, exporter
├── Extensions/WebApplicationExtensions.cs # call AddBotFarmTelemetry
├── Telemetry/
│   ├── LoggingMetricExporter.cs       # once-per-outage failure log
│   ├── AppMemoryMetrics.cs            # working set, GC heap, threshold gauges
│   └── TelemetryStartupService.cs     # create gauges, record zeros for each bot
├── Pages/HealthDashboard.razor(.cs)   # + MetricsLink to the memory panel
├── appsettings.json                   # + empty Telemetry section
└── appsettings.Development.json       # + local endpoint and dashboard URL

BotFarm.Shared/
├── Components/MetricsLink.razor       # app bar icon link, hidden when no address
├── Components/BotDashboard.razor      # + MetricsLink for the bot, in the app bar actions
└── AdminLanguages/...                 # Metrics.Link in 3 languages

monitoring/
├── docker-compose.yml                 # grafana/otel-lgtm:0.41.1, localhost ports, volume
└── grafana/
    ├── dashboards/
    │   └── botfarm-dashboard.json     # uid botfarm, 5 panels, event annotations
    └── dashboards.yaml                # provisioning provider

docker-compose.yml                     # + Telemetry__* passthrough for production
docker-compose.override.yml            # + endpoint to host.docker.internal, extra_hosts
Directory.Packages.props               # + 3 package versions
README.md                              # metrics section: local stack, Grafana Cloud setup

tests/
├── BotFarm.Core.UnitTests/Services/UpdateServiceTests.cs       # Describe classification
├── BotFarm.Core.UnitTests/Telemetry/                           # UpdateMetrics, MetricsLinks, options validation
├── TestBot.UnitTests/Controllers/UpdateControllerTests.cs      # Post outcomes → measurements
├── BotFarm.UnitTests/Telemetry/                                # registration on/off, precedence, exporter log, gauges, startup zeros, dashboard file
├── BotFarm.Shared.UnitTests/Components/MetricsLinkRenderTests.cs # shown, hidden, URL
├── BotFarm.UnitTests/Pages/HealthDashboardRenderTests.cs       # memory link
└── BotFarm.IntegrationTests/Host/
    ├── WebhookPipelineTests.cs        # webhook request → measurements
    └── TelemetryExportTests.cs        # export reaches a fake OTLP endpoint with headers
```

**Structure Decision**: Use the existing four-layer layout. Metrics code goes in a `Telemetry/` folder in Core and in the host, and only `IMetricsLinks` goes in `Abstractions/`. The local stack gets its own `monitoring/` folder, so it stays optional and separate from the app's compose files.

## Complexity Tracking

No constitution violations to justify.
