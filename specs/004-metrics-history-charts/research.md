# Research: Metrics History and Charts

Decisions behind [plan.md](plan.md). Each entry gives the decision, why, and what else was considered.

## R1. Instrumentation API

**Decision**: Take measurements with .NET's built-in `System.Diagnostics.Metrics`. A singleton `UpdateMetrics` in BotFarm.Core creates the meter `BotFarm` through `IMeterFactory` and owns all bot instruments.

**Rationale**:
- It is part of the base library, so BotFarm.Core needs no new package.
- With no listener attached, `Counter<T>.Add` and `Histogram<T>.Record` return almost at once. This is what makes "export off costs nothing" (FR-011, SC-008) true without a separate switch.
- `IMeterFactory` is registered by the host by default and gives each DI container its own meter, so tests stay isolated.

**Alternatives considered**:
- Calling the OpenTelemetry API directly from Core: it adds a package to Core for no benefit, because the SDK listens to `System.Diagnostics.Metrics` anyway.
- A static `Meter`: shared across test hosts, so measurements from one test leak into another.

## R2. Exporter packages

**Decision**: Add `OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.21.0, the latest stable versions, to the BotFarm host project only. Export over OTLP with HTTP/protobuf.

**Rationale**:
- These are the reference SDK and exporter, and they read the standard `OTEL_*` environment variables.
- Grafana Cloud's OTLP gateway accepts HTTP/protobuf. The local `otel-lgtm` container accepts it on port 4318.
- Only the host references them, so Core, Shared and the bots are unchanged (constitution I).

**Alternatives considered**:
- Grafana's `Grafana.OpenTelemetry` distribution: it brings automatic instrumentation for ASP.NET Core, HttpClient and more. That goes against FR-010 and adds series and dependencies we don't use.
- The Prometheus scrape exporter: Grafana Cloud would need an agent to scrape the app, and the app would need a public `/metrics` endpoint.

## R3. Turning export on, and precedence

**Decision**: The host registers OpenTelemetry only when an endpoint is known:
- `Telemetry:OtlpEndpoint`, BotFarm's own setting; or
- `OTEL_EXPORTER_OTLP_ENDPOINT`, read through `IConfiguration`, where the default environment-variable provider places it.

When both are set, `Telemetry:OtlpEndpoint` wins. When neither is set, `AddOpenTelemetry` is never called, so no meter provider, reader, exporter or timer exists.

Endpoint handling:
- `Telemetry:OtlpEndpoint` is a base URL, as Grafana Cloud shows it (`…/otlp`). The host appends `/v1/metrics` itself, because the .NET exporter uses an endpoint set in code exactly as given and only adds the signal path to one read from the environment variable.
- `Telemetry:OtlpHeaders` sets the exporter's headers when present. Otherwise `OTEL_EXPORTER_OTLP_HEADERS` applies.

**Rationale**:
- This meets FR-011's rules: the app's setting first, the standard variable second, and true zero cost when neither is set.
- Reading the variable through `IConfiguration` lets tests cover precedence without changing process environment variables.

**Alternatives considered**:
- Always registering the SDK and relying on an empty endpoint: the exporter would fall back to `localhost:4318` and keep failing.
- A separate `Telemetry:Enabled` flag: a second setting that could disagree with the endpoint.

## R4. Logging export failures once per outage

**Decision**: `LoggingMetricExporter` derives from `OtlpMetricExporter` and overrides `Export` to call the base export and hand the result to a small `ExportFailureLog`.
- On the first `ExportResult.Failure` after a success, or after startup, it logs one warning with the endpoint's host only. Repeated failures are not logged.
- On the next success it logs one information line saying export recovered.
- A `PeriodicExportingMetricReader` drives it and sets its provider directly, so the resource reaches every payload.
- Its HTTP client keeps idle connections for 30 s only. The default keeps them for 60 s, the same as the export interval, so now and then an export reused a connection the receiver had just closed and failed: 2 of 14 exports through `host.docker.internal` failed with the default client, and none with the 30 s limit.

**Rationale**:
- The SDK reports exporter failures only through its own `EventSource`, which the app's logs don't see. Without the wrapper, an outage would be silent, which goes against FR-014.
- Headers are never logged, so the token stays out of logs (FR-013).

**Alternatives considered**:
- An `EventListener` on the SDK's event source: it depends on undocumented event ids and messages.
- OpenTelemetry self-diagnostics: it writes a file, not the app's log.

## R5. Handling-time buckets

**Decision**: Record handling time in seconds as `Histogram<double>`. Set its bucket boundaries with `InstrumentAdvice<double>`: 0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30 and 60 (FR-003).

**Rationale**:
- The advice sits next to the instrument in Core, and the SDK applies it without a view in the host.
- With 11 boundaries, a bot produces 14 series for this histogram (12 buckets plus sum and count), which is cheap.

**Alternatives considered**:
- The SDK's default boundaries: they are designed for milliseconds and are too coarse below 1 s when recording in seconds.
- Exponential histograms: Grafana Cloud supports them, but the local stack and `histogram_quantile` handle classic buckets more predictably.

## R6. Reporting zero for idle bots

**Decision**: When export is on, a hosted service runs once at startup and calls `Add(0)` for every registered bot:
- for each of the three update kinds;
- for each of the five outcomes;
- for each command key in the bot's `IUpdateService.CommandKeys`, and for `(unknown)`.

This creates every throughput, outcome and command series straight away (FR-009).

**Rationale**:
- OpenTelemetry exports a series only once it has been recorded. Without this, an idle bot shows "no data" instead of zero.
- A series that first appears with value 1 has no earlier sample, so `increase()` never counts that first call. Starting every known series at 0 means the first call after startup is counted.
- Counting registered bots rather than enabled bots also covers bots that are disabled later: they show zero, as the spec's edge case requires.
- The handling-time histogram can't be started at zero without recording an observation, so `rate()` misses the first observation of each process. That is acceptable for percentiles.

**Alternatives considered**:
- Observable counters that read our own totals: this duplicates the SDK's aggregation.

## R7. Temporality and restarts

**Decision**: Use the default cumulative temporality.

**Rationale**:
- Prometheus-compatible storage, which both Grafana Cloud and `otel-lgtm` use, expects cumulative counters.
- `increase()` and `rate()` treat the drop to zero after a restart as a counter reset, so totals stay correct across restarts and no negative spike appears (US1, scenario 4).

**Alternatives considered**:
- Delta temporality: Grafana Cloud converts it, but the local stack and PromQL work best with cumulative.

## R8. Memory measurements

**Decision**: The host adds three observable gauges, `botfarm.memory.working_set`, `botfarm.memory.gc_heap` and `botfarm.memory.threshold`, in bytes. They read:
- `Environment.WorkingSet`;
- `GC.GetGCMemoryInfo().HeapSizeBytes`;
- `IOptionsMonitor<MemoryCheckOptions>.Get(HealthCheckExtensions.MemoryCheckName).Threshold`.

These are the same sources `MemoryHealthCheck` reports.

**Rationale**:
- The chart then matches the health check exactly, and the threshold line follows configuration as it was at each point in time (US3, scenario 2).
- Observable gauges are read only when the reader collects, so they cost nothing when export is off. They are created only when export is on.

**Alternatives considered**:
- The runtime meter's `dotnet.process.memory.working_set` and `dotnet.gc.last_collection.heap.size`: the second is split by generation and measured differently from the health check, and neither carries the threshold. They are still exported with the runtime metrics (R9) for ad-hoc use.

## R9. Runtime and web server metrics

**Decision**: Register the meters `BotFarm` and `System.Runtime` only.

**Rationale**:
- The SDK exports only the meters it is told about, so ASP.NET Core's `Microsoft.AspNetCore.*` and `System.Net.Http` meters are left out without views (FR-010).
- `System.Runtime`, built into .NET 9 and later, covers garbage collection, thread pool, CPU, process memory and exception counts in a few dozen series.

**Alternatives considered**:
- Adding ASP.NET Core metrics and dropping their labels with views: more configuration for data we don't chart.

## R10. Metric names as stored

**Decision**: Instrument names and units are chosen so the names after OTLP-to-Prometheus translation are predictable, and the dashboard queries those names:

| Instrument | Unit | Stored as |
|---|---|---|
| `botfarm.updates` | `{update}` | `botfarm_updates_total` |
| `botfarm.update.outcomes` | `{delivery}` | `botfarm_update_outcomes_total` |
| `botfarm.update.duration` | `s` | `botfarm_update_duration_seconds_bucket`, `_sum` and `_count` |
| `botfarm.commands` | `{call}` | `botfarm_commands_total` |
| `botfarm.memory.working_set` | `By` | `botfarm_memory_working_set_bytes` |
| `botfarm.memory.gc_heap` | `By` | `botfarm_memory_gc_heap_bytes` |
| `botfarm.memory.threshold` | `By` | `botfarm_memory_threshold_bytes` |

The resource sets `service.name=BotFarm`, which appears as the `job` label.

**Rationale**: This is the standard translation both backends apply by default: dots become underscores, a unit suffix is added, and counters get `_total`.

**Verification gap**: Grafana Cloud can be set to keep the original OTel names. The quickstart includes a check that the names above appear in the hosted stack before the dashboard is imported.

## R11. Local monitoring stack

**Decision**: Add `monitoring/docker-compose.yml` with one service, `grafana/otel-lgtm:0.41.1`:
- **Ports:** 3000 (Grafana), 4317 (OTLP/gRPC) and 4318 (OTLP/HTTP), all bound to `127.0.0.1`.
- **Data:** a named volume on `/data`, so history survives container restarts.
- **Dashboard:** `monitoring/grafana/dashboards/botfarm-dashboard.json` and a provisioning file `monitoring/grafana/dashboards.yaml`, mounted as the image's README describes for this version: the provisioning file at `/otel-lgtm/grafana/conf/provisioning/dashboards/custom.yaml`, and the `dashboards/` folder, not the single file, at the provider's path `…/dashboards/custom`. A folder mount lets Grafana pick up dashboard edits within about 10 s; a single-file bind mount keeps the old file once an editor replaces it.
- **Access:** `GF_AUTH_ANONYMOUS_ENABLED=true` and `GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`, so there is no sign-in (FR-020).

The command is:

```bash
docker compose -f monitoring/docker-compose.yml up -d
```

**Rationale**:
- One container holds a collector, Prometheus-compatible storage and Grafana.
- Its own compose file keeps it runnable on its own, for example beside `dotnet run`. `docker-compose.override.yml` includes it, so the containerised development run, including the IDE's Docker Compose launch, starts it too.
- A fixed volume name, `botfarm-otel-lgtm-data`, keeps one history whichever way the stack is started.
- Ports are bound to localhost, so anonymous Admin access never leaves the developer's machine (constitution II).

Wiring the app to it:
- `appsettings.Development.json` sets `Telemetry:OtlpEndpoint` to `http://localhost:4318` and `Telemetry:DashboardUrl` to `http://localhost:3000/d/botfarm/botfarm`.
- `docker-compose.override.yml` sets `Telemetry__OtlpEndpoint=http://host.docker.internal:4318` for the containerised development app. `extra_hosts: host.docker.internal:host-gateway` lets that name work on Linux.

**Alternatives considered**:
- Separate Prometheus and Grafana containers: they need an OTel collector or Prometheus's OTLP receiver flags, plus a datasource provisioning file.
- Copying the service into `docker-compose.override.yml`: two definitions of the same stack to keep in step. `include` reuses the one file.
- The .NET Aspire dashboard: it keeps no history across restarts and is not Grafana, so the dashboard couldn't be tried locally.

## R12. Dashboard definition

**Decision**: Commit one Grafana dashboard JSON with `uid` `botfarm`, so both the local and the hosted URL are `/d/botfarm/botfarm`. Its parts:
- **Variables:**
  - `datasource`: a Prometheus datasource picker, used by every panel, so the same file works with the local datasource and Grafana Cloud's `grafanacloud-…-prom`.
  - `bot`: built from `label_values(botfarm_updates_total, bot)`.
- **Panels, in FR-016 order:**
  1. **Throughput:** stacked bars of `sum by (kind) (increase(botfarm_updates_total{bot="$bot"}[$__interval]))`, with the panel's Min interval set to `2m`.
  2. **Errors:** lines of `sum by (outcome) (increase(botfarm_update_outcomes_total{bot="$bot"}[$__interval]))`, with Min interval `2m`.
  3. **Handling time:** `histogram_quantile(0.5 | 0.95, sum by (le) (rate(botfarm_update_duration_seconds_bucket{bot="$bot"}[$__rate_interval])))`, plus `vector(60)` with the legend "Webhook deadline" and a dashed style.
  4. **Command usage:** a bar gauge from the instant query `sort_desc(sum by (command) (increase(botfarm_commands_total{bot="$bot"}[$__range])))`.
  5. **App memory:** the three memory gauges, with the threshold dashed. Its panel id is fixed so the health dashboard can link to it.
- **Display:** units are set per panel (short, s, and IEC bytes so memory reads in the same megabytes as `ThresholdMb`). Single-series panels hide the legend. Time-series panels use tooltip mode All, so hovering shows every series at that time. The default range is 24 h, and the time picker offers 1 h, 24 h and 7 d (FR-017, FR-018).
- **Hosted copy:** the dashboard is imported once by hand through Grafana's Import, as described in the README.

**Rationale**:
- One file serves both environments.
- Drawing the deadline as a query gives a labelled line on the same axis, while Grafana's thresholds have no legend entry.
- `increase()` needs two samples in a window and extrapolates across it. With a 60 s export interval, a 2 m minimum interval stops gaps while the app is running. Bars are therefore estimates; exact totals come from the raw counters.
- A unit test checks the file's panel order, the metric names it uses and the deadline value against `Constants.TelegramWebhookTimeout`, so code and dashboard can't drift apart.

**Alternatives considered**:
- Pushing the dashboard to Grafana Cloud from the app or CI: it needs another token with write access to dashboards.
- Grafana's newer dashboard schemas: the classic JSON model imports into both the local and the hosted version.

## R13. Admin dashboard links

**Decision**: `Telemetry:DashboardUrl` holds the dashboard's address. Core binds it to `TelemetryOptions`, and a small `IMetricsLinks` service builds the two links:
- **Bot dashboards:** the URL with `var-bot={botName}` added.
- **Health dashboard:** the URL with `viewPanel=panel-{memoryPanelId}` added.

Query strings are added with `QueryHelpers.AddQueryString`, so a URL that already has `?orgId=1` stays valid. A `MetricsLink` component in BotFarm.Shared renders it as an app bar icon button with a translated "Metrics" tooltip that opens in a new tab, placed through an `ActionsSection` outlet in `MainLayout` so it adds no row above the dashboard cards. It renders nothing when no address is set (FR-019).

**Rationale**:
- `var-<name>` and `viewPanel` are Grafana's URL parameters.
- Current Grafana versions, both Cloud and the local image, address panels as `panel-<id>`.

**Alternatives considered**:
- Embedding panels in an iframe: it needs anonymous or public dashboards on Grafana Cloud and a relaxed frame policy, which would bypass the hosted service's own sign-in.

## R14. Testing approach

**Decision**:
- **Recording:** controller and pipeline tests read measurements with `MetricCollector<T>` from `Microsoft.Extensions.Diagnostics.Testing` 11.0.1, a test-only package. No exporter is involved.
- **Registration:** tests build the host's services from in-memory configuration and check that no `MeterProvider` is registered when no endpoint is set, and which endpoint wins when both are set.
- **Export:** one integration test starts a local Kestrel listener on a free port as a fake OTLP endpoint. It points `Telemetry:OtlpEndpoint` at it, flushes the meter provider, and checks the request path is `/v1/metrics`, the configured header arrived, and the body is non-empty.
- **Dashboard:** a unit test parses `monitoring/grafana/dashboards/botfarm-dashboard.json`, which the test project copies to its output.

**Rationale**:
- Every test runs without Grafana, Telegram or the network (constitution III).
- The fake endpoint catches the signal-path rule in R3, which is the most likely mistake.

**Alternatives considered**:
- A Testcontainers `otel-lgtm` test: a large image to pull, and slow, to prove what the fake endpoint already proves.

## R15. Labels and series count

**Decision**: Bot measurements carry only these labels:
- `bot`: the registered bot name;
- `kind`: `command`, `callback_query` or `other`;
- `outcome`: `failed`, `retried`, `dropped`, `duplicate` or `rejected`;
- `command`: a built-in or registered command key, or `(unknown)`.

**Rationale**:
- Every label value comes from a fixed set, so the series count depends only on the number of bots and commands (FR-008).
- `(unknown)` can't clash with a real command, because Telegram command names can't contain parentheses.
- For 5 bots with 10 registered commands each, the estimate is under 300 series:

| Source | Series |
|---|---|
| Throughput | 15 |
| Outcomes | 25 |
| Duration histogram | 70 |
| Commands | up to 65 |
| Memory | 3 |
| Runtime meter | about 60 |

That is well under SC-003's 2,000 and the free plan's 10,000.

## R16. Bot events as dashboard annotations

**Decision**: Count backups, restores, operator on/off changes and dropped pending updates on a counter `botfarm.bot.events`, labelled by bot and event, recorded where `MongoDbBackupService` and `BotControlCoordinator` finish those actions. The dashboard marks them with one Prometheus annotation per event: `sum(increase(botfarm_bot_events_total{bot="$bot", event="…"}[2m])) > 0` at a 2 m step, titled with the event's name, green or blue for success and red for failure.

**Rationale**:
- It uses the existing export path, so it needs no new settings, tokens or dependencies.
- Like the other counters, each event series starts at zero at startup, so the first event after a restart is not lost to `increase()`.
- A 2 m step with a 2 m window matches the panels' minimum interval: with exports exactly 60 s apart, each window holds two samples and each event is marked once.
- Markers land within the 2-minute interval of the event rather than at its exact second, which is enough to line events up with the charts.
- App restarts need no counter: each process exports under its own `service_instance_id`, so `count by (service_instance_id) (botfarm_memory_working_set_bytes) unless … offset 2m` is non-empty only in the step where a new instance first reports. It uses the memory gauge because it reports every collection, whatever the bots do.

**Alternatives considered**:
- Exporting logs to Loki and annotating from log lines: a much larger change, and logs are out of scope.
- Posting annotations to Grafana's HTTP API: exact timestamps, but it needs a second Grafana token with write access and ties the app to Grafana rather than OTLP.
