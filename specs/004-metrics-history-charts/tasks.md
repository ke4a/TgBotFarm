---

description: "Task list for Metrics History and Charts"
---

# Tasks: Metrics History and Charts

**Input**: Design documents from `/specs/004-metrics-history-charts/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/metrics-contracts.md](./contracts/metrics-contracts.md), [quickstart.md](./quickstart.md)

**Tests**: Included. The constitution (principle III) requires focused behavior tests for every change. Write each story's tests first and make sure they fail before implementing.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US5)

## Path Conventions

The repository's own layout: `BotFarm.Core/`, `BotFarm.Shared/`, `BotFarm/` (host), `TestBot/`, `monitoring/`, `tests/<project>/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Packages and constants every later phase uses.

- [X] T001 Add `OpenTelemetry.Extensions.Hosting` 1.21.0, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.21.0 and `Microsoft.Extensions.Diagnostics.Testing` 11.0.1 to `Directory.Packages.props` (main application and test sections)
- [X] T002 [P] Reference both OpenTelemetry packages in `BotFarm/BotFarm.csproj` (host only; no other project gets them)
- [X] T003 [P] Reference `Microsoft.Extensions.Diagnostics.Testing` in `tests/BotFarm.Core.UnitTests/BotFarm.Core.UnitTests.csproj`, `tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj`, `tests/TestBot.UnitTests/TestBot.UnitTests.csproj` and `tests/BotFarm.IntegrationTests/BotFarm.IntegrationTests.csproj`
- [X] T004 [P] Add `Constants.TelegramWebhookTimeout` (60 s, Telegram's webhook response limit) and a `Constants.Telemetry` class with `SectionName = "Telemetry"`, `UnknownCommand = "(unknown)"`, `MetricsPath = "/v1/metrics"` and `MemoryPanelId = 5` in `BotFarm.Core/Constants.cs`; update the doc comment of `TelegramBotClientFactory.DefaultTimeout` in `BotFarm.Core/Services/TelegramBotClientFactory.cs` to refer to `Constants.TelegramWebhookTimeout`, keeping its 25 s value

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Classification, instruments, configuration, the on/off export registration and the local stack. Every story depends on these.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Tests

- [X] T005 [P] Add `Describe` tests to `tests/BotFarm.Core.UnitTests/Services/UpdateServiceTests.cs`: `/start`, `/changelanguage`, a registered command, an unknown command (`(unknown)`), `/help@ThisBot` → `help`, a command for another bot → `Other`, upper-case command → lower-case key, callback query, plain message, chat member update; `Describe` never throws on an update with no message; `CommandKeys` lists `start`, `changelanguage` and the registered keys in lower case
- [X] T006 [P] Add `tests/BotFarm.Core.UnitTests/Telemetry/UpdateMetricsTests.cs` using `MetricCollector<T>` over a `ServiceCollection().AddMetrics()` meter factory: `RecordProcessed` adds 1 to `botfarm.updates` with `bot` and `kind` tags, records the duration in seconds on `botfarm.update.duration`, and adds to `botfarm.commands` with the key only for commands; `RecordOutcome` tags each `UpdateOutcome` with its lower-case value (`failed`, `retried`, `dropped`, `duplicate`, `rejected`); `RecordZeros` adds 0 for all 3 kinds, 5 outcomes, every given command key and `(unknown)`; the histogram advertises the 11 boundaries from FR-003; no other tags are ever set
- [X] T007 [P] Add `tests/BotFarm.Core.UnitTests/Telemetry/TelemetryOptionsTests.cs`: empty values are valid; `OtlpEndpoint` and `DashboardUrl` must be absolute `http`/`https` URLs when set; startup fails on an invalid value
- [X] T008 [P] Add `tests/BotFarm.UnitTests/Telemetry/ExportFailureLogTests.cs` for the once-per-outage log used by `LoggingMetricExporter`: the first failure logs one warning naming only the endpoint's host; further failures log nothing; the first success after a failure logs one recovery line; headers never appear in any log message
- [X] T009 [P] Add `tests/BotFarm.UnitTests/Telemetry/TelemetryRegistrationTests.cs` building services from in-memory configuration: with no endpoint no `MeterProvider` and no telemetry hosted service is registered; `Telemetry:OtlpEndpoint` alone turns export on with endpoint `<base>/v1/metrics` and HTTP/protobuf; `OTEL_EXPORTER_OTLP_ENDPOINT` alone turns it on; when both are set `Telemetry:OtlpEndpoint` wins; `Telemetry:OtlpHeaders` sets the headers and otherwise they are left to `OTEL_EXPORTER_OTLP_HEADERS`; the configured meters are `BotFarm` and `System.Runtime` only

### Implementation

- [X] T010 [P] Create `BotFarm.Core/Telemetry/UpdateDescription.cs` with `UpdateKind` (`Command`, `CallbackQuery`, `Other`), `UpdateDescription(UpdateKind Kind, string? CommandKey)` and `UpdateOutcome` (`Failed`, `Retried`, `Dropped`, `Duplicate`, `Rejected`), with the tag value of each member
- [X] T011 Add the `Describe(Update)` and `CommandKeys` default interface members to `BotFarm.Core/Abstractions/IUpdateService.cs` as contracts §1 states (depends on T010)
- [X] T012 Override `Describe` in `BotFarm.Core/Base/UpdateService.cs` by sharing the routing `ProcessUpdate` already uses (the command-for-this-bot check and built-in/registered lookup), so classification and routing cannot disagree; no I/O, no state change, never throws; override `CommandKeys` with the built-in and registered keys (depends on T011)
- [X] T013 Create `BotFarm.Core/Telemetry/UpdateMetrics.cs`: meter `BotFarm` from `IMeterFactory`; instruments `botfarm.updates`, `botfarm.update.outcomes`, `botfarm.update.duration` (seconds, boundaries via `InstrumentAdvice<double>`) and `botfarm.commands` with the units in data-model §1; `RecordProcessed`, `RecordOutcome`, `RecordZeros`; tags built with `TagList`; every method catches and ignores its own exceptions (depends on T010)
- [X] T014 [P] Create `BotFarm.Core/Telemetry/TelemetryOptions.cs` (`OtlpEndpoint`, `OtlpHeaders`, `DashboardUrl`) with its validator, and register `UpdateMetrics` as a singleton and the options bound to `Constants.Telemetry.SectionName` with `ValidateOnStart` in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`; update `tests/BotFarm.Core.UnitTests/Extensions/CoreServicesDependencyInjectionTests.cs` for the new registrations
- [X] T015 [P] Create `BotFarm/Telemetry/LoggingMetricExporter.cs` as research R4 describes
- [X] T016 Create `BotFarm/Extensions/TelemetryExtensions.cs` with `AddBotFarmTelemetry(IServiceCollection, IConfiguration)` as contracts §3 states: return without registering anything when neither endpoint is set; otherwise `AddOpenTelemetry().ConfigureResource(service.name=BotFarm, service.version=<informational version>).WithMetrics(...)` with meters `BotFarm` and `System.Runtime`, one `PeriodicExportingMetricReader` (60 s) around `LoggingMetricExporter(OtlpMetricExporter)`, endpoint and headers per research R3; call it from `AddBotFarm` in `BotFarm/Extensions/WebApplicationExtensions.cs` (depends on T014, T015)
- [X] T017 [P] Add an empty `Telemetry` section (`OtlpEndpoint`, `OtlpHeaders`, `DashboardUrl`) to `BotFarm/appsettings.json`, and pass `Telemetry__OtlpEndpoint=${OTLP_ENDPOINT:-}`, `Telemetry__OtlpHeaders=${OTLP_HEADERS:-}` and `Telemetry__DashboardUrl=${GRAFANA_DASHBOARD_URL:-}` through in `docker-compose.yml`, with a comment that the headers hold the Grafana Cloud token
- [X] T018 [P] Create `monitoring/docker-compose.yml` (`grafana/otel-lgtm:0.41.1`; ports 3000, 4317, 4318 bound to `127.0.0.1`; named volume on `/data`; `GF_AUTH_ANONYMOUS_ENABLED=true`, `GF_AUTH_ANONYMOUS_ORG_ROLE=Admin`; mounts for the dashboard file and provisioning file) and `monitoring/grafana/dashboards.yaml` (file provider at `/otel-lgtm/grafana/conf/provisioning/dashboards/custom`), per research R11
- [X] T019 Add only a `Telemetry` section with `OtlpEndpoint` `http://localhost:4318` and `DashboardUrl` `http://localhost:3000/d/botfarm/botfarm` to `BotFarm/appsettings.Development.json`, leaving every other value in the file as it is; set `Telemetry__OtlpEndpoint=http://host.docker.internal:4318` and `extra_hosts: ["host.docker.internal:host-gateway"]` for `botfarm` in `docker-compose.override.yml`

**Checkpoint**: Updates can be classified and measured, export turns on only with an endpoint, and the local stack starts. Run T005–T009.

---

## Phase 3: User Story 1 - Operator sees a bot's update throughput over time (Priority: P1) 🎯 MVP

**Goal**: Processed deliveries are counted by bot and kind, exported, charted in the throughput panel, and reachable from the bot's admin dashboard.

**Independent Test**: With the local stack running, send TestBot a known mix of commands, button presses and messages; after the next export the counter totals rose by exactly those counts by kind and the throughput panel shows them, idle bots show zero, and a restart causes no negative value (quickstart §2–§3).

### Tests for User Story 1

- [X] T020 [P] [US1] Add tests to `tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs` (with `MetricCollector<T>`): a processed command, callback query and plain message each add 1 to `botfarm.updates` with the right `kind` and record one duration; a processed delivery that throws still records its kind and duration; rejected and duplicate deliveries record no throughput
- [X] T021 [P] [US1] Add a test to `tests/BotFarm.IntegrationTests/Host/WebhookPipelineTests.cs`: webhook requests through the real host pipeline produce the expected `botfarm.updates` measurements (add update builders to `tests/BotFarm.IntegrationTests/Support/TelegramUpdates.cs` as needed)
- [X] T022 [P] [US1] Add `tests/BotFarm.IntegrationTests/Host/TelemetryExportTests.cs`: start a local Kestrel listener on a free port as a fake OTLP endpoint, run the app factory with `Telemetry:OtlpEndpoint` pointing at it and `Telemetry:OtlpHeaders` set, send one webhook update, force-flush the `MeterProvider`, and check a POST reached `/v1/metrics` with the configured header and a non-empty protobuf body; stopping the host after recording one more update sends a final export containing it; also check that with no endpoint the factory's services hold no `MeterProvider` (extend `tests/BotFarm.IntegrationTests/Support/BotFarmAppFactory.cs` to accept extra configuration)
- [X] T023 [P] [US1] Add `tests/BotFarm.UnitTests/Telemetry/TelemetryStartupServiceTests.cs`: on start, `RecordZeros` runs once for every registered bot module, including disabled ones, with that bot's `IUpdateService.CommandKeys`
- [X] T024 [P] [US1] Add `tests/BotFarm.UnitTests/Telemetry/DashboardDefinitionTests.cs` reading `monitoring/grafana/dashboards/botfarm-dashboard.json` (copied to the test output by `tests/BotFarm.UnitTests/BotFarm.UnitTests.csproj`): uid is `botfarm`; variables `datasource` (Prometheus) and `bot` exist; every panel uses `${datasource}`; every metric name in every query is one of the names in data-model §1; the default range is 24 h and the time picker offers 1 h, 24 h and 7 d; single-series panels hide the legend; every time-series panel has tooltip mode `multi`; the throughput and errors panels have Min interval of at least `2m`; the throughput panel is first, stacked, unit short, grouped by `kind`
- [X] T025 [P] [US1] Add `tests/BotFarm.Core.UnitTests/Telemetry/MetricsLinksTests.cs`: no `DashboardUrl` → both links null; `ForBot` adds an encoded `var-bot`; an existing query string such as `?orgId=1` is kept
- [X] T026 [P] [US1] Add `tests/BotFarm.Shared.UnitTests/Components/MetricsLinkRenderTests.cs` and extend `tests/BotFarm.Shared.UnitTests/Components/BotDashboardRenderTests.cs`: the link renders with the translated text, the URL, `target="_blank"` and `rel="noopener noreferrer"`; nothing renders when the URL is null; `BotDashboard` shows the bot's link

### Implementation for User Story 1

- [X] T027 [US1] In `BotFarm.Core/Controllers/UpdateControllerBase.cs`, add a `[FromServices] UpdateMetrics metrics` parameter to `Post`; after the duplicate check take a timestamp, and when `ProcessUpdate` returns or throws call `RecordProcessed` with `_updateService.Describe(update)` and the elapsed time; recording never changes the response (update the doc comment) (depends on T013)
- [X] T028 [P] [US1] Create `BotFarm/Telemetry/TelemetryStartupService.cs`, a hosted service registered by `AddBotFarmTelemetry` only when export is on, that calls `RecordZeros` for every `IBotModule` at start with the keys from that bot's `IUpdateService` (resolved through its existing keyed registration)
- [X] T029 [P] [US1] Create `monitoring/grafana/dashboards/botfarm-dashboard.json` with uid `botfarm`, title "BotFarm", the `datasource` and `bot` variables, default range 24 h, time options 1 h/24 h/7 d, and panel 1 "Throughput" (stacked bars, `sum by (kind) (increase(botfarm_updates_total{bot="$bot"}[$__interval]))`, legend names Commands/Callback queries/Other, Min interval `2m`, tooltip mode All) per research R12
- [X] T030 [P] [US1] Create `BotFarm.Core/Abstractions/IMetricsLinks.cs` and `BotFarm.Core/Telemetry/MetricsLinks.cs` per contracts §4, registered in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`
- [X] T031 [US1] Create `BotFarm.Shared/Components/MetricsLink.razor` per contracts §5, add `Metrics.Link` to `BotFarm.Shared/AdminLanguages/BotFarm.Shared/en-US.json`, `ru-RU.json` and `uk-UA.json`, add an `ActionsSection` outlet to the app bar in `BotFarm.Shared/MainLayout.razor`, and put `<MetricsLink Href="@Links.ForBot(BotName)" />` in it from `BotFarm.Shared/Components/BotDashboard.razor` (depends on T030)

**Checkpoint**: Throughput is measured, exported and charted; bot dashboards link to it. Run T020–T026 and quickstart §2–§3.

---

## Phase 4: User Story 2 - Operator spots error spikes and slow handling (Priority: P2)

**Goal**: Every delivery outcome is counted, and handling time is charted as p50 and p95 against the webhook deadline.

**Independent Test**: Make TestBot fail updates (permanent and temporary), send a duplicate and a wrong-secret request, and slow its handling; the errors panel shows the outcome counts from US2 scenario 1 and the handling time panel shows p50, p95 and the 60 s deadline line (quickstart §4).

### Tests for User Story 2

- [X] T032 [P] [US2] Add tests to `tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs` covering every row of the outcome table in data-model §1: invalid or missing secret → `rejected`; not ready → `rejected`; duplicate → `duplicate`; temporary error released for retry → `failed` and `retried`; temporary error on the last attempt → `failed` and `dropped`; permanent error → `failed`; success → no outcome; a permanent failure plus a temporary update failing 5 times gives 7 failed, 4 retried, 1 dropped (US2 scenario 1); duplicates are never counted as failed
- [X] T033 [P] [US2] Add tests to `tests/BotFarm.IntegrationTests/Host/WebhookPipelineTests.cs`: a wrong-secret request and a repeated update id through the real pipeline record `rejected` and `duplicate`
- [X] T034 [P] [US2] Extend `tests/BotFarm.UnitTests/Telemetry/DashboardDefinitionTests.cs`: panel 2 "Errors" groups `botfarm_update_outcomes_total` by `outcome`; panel 3 "Handling time" has unit seconds, p50 and p95 `histogram_quantile` queries over `botfarm_update_duration_seconds_bucket`, and a "Webhook deadline" series whose value equals `Constants.TelegramWebhookTimeout.TotalSeconds`; no panel has a second y-axis

### Implementation for User Story 2

- [X] T035 [US2] In `BotFarm.Core/Controllers/UpdateControllerBase.cs`, call `RecordOutcome` for each result of `Post` per the outcome table in data-model §1 (depends on T027)
- [X] T036 [US2] Add panel 2 "Errors" (lines, `sum by (outcome) (increase(botfarm_update_outcomes_total{bot="$bot"}[$__interval]))`, legend names Failed/Retried/Dropped/Duplicates skipped/Webhook rejected, Min interval `2m`) and panel 3 "Handling time" (p50 and p95 lines, unit s, plus `vector(60)` named "Webhook deadline" drawn dashed) to `monitoring/grafana/dashboards/botfarm-dashboard.json` (depends on T029)

**Checkpoint**: Errors and handling time are charted. Run T032–T034 and quickstart §4.

---

## Phase 5: User Story 3 - Operator watches app memory for slow leaks (Priority: P3)

**Goal**: Working set, GC heap and the threshold are exported and charted, and the health dashboard links to the chart.

**Independent Test**: With the local stack running, open the memory panel from the health dashboard's Metrics link; it shows both memory lines and the threshold, and a changed threshold steps the line after a restart (quickstart §5).

### Tests for User Story 3

- [X] T037 [P] [US3] Add `tests/BotFarm.UnitTests/Telemetry/AppMemoryMetricsTests.cs` (with `MetricCollector<T>` and `RecordObservableInstruments`): the three gauges report positive working set and GC heap in bytes and the threshold from `IOptionsMonitor<MemoryCheckOptions>.Get(HealthCheckExtensions.MemoryCheckName)`, following a changed option value
- [X] T038 [P] [US3] Extend `tests/BotFarm.UnitTests/Telemetry/DashboardDefinitionTests.cs`: the last panel is "App memory" with id `Constants.Telemetry.MemoryPanelId`, unit `bytes` (IEC, so the threshold reads as configured), and series for working set, GC heap and the threshold
- [X] T039 [P] [US3] Extend `tests/BotFarm.Core.UnitTests/Telemetry/MetricsLinksTests.cs` (`ForAppMemory` adds `viewPanel=panel-5`) and `tests/BotFarm.UnitTests/Pages/HealthDashboardRenderTests.cs` (the health dashboard shows the memory link when configured and no link otherwise)

### Implementation for User Story 3

- [X] T040 [US3] Create `BotFarm/Telemetry/AppMemoryMetrics.cs` per research R8, registered by `AddBotFarmTelemetry` only when export is on and created by `TelemetryStartupService` at start (depends on T028)
- [X] T041 [P] [US3] Add panel 5 "App memory" (id 5; lines `botfarm_memory_working_set_bytes`, `botfarm_memory_gc_heap_bytes`, and `botfarm_memory_threshold_bytes` dashed; unit `bytes` (IEC, so the threshold reads as configured); not filtered by `bot`) to `monitoring/grafana/dashboards/botfarm-dashboard.json` (depends on T036)
- [X] T042 [P] [US3] Put `<MetricsLink Href="@Links.ForAppMemory()" />` in the app bar's `ActionsSection` from `BotFarm/Pages/HealthDashboard.razor`, injecting `IMetricsLinks` in `BotFarm/Pages/HealthDashboard.razor.cs`

**Checkpoint**: Memory is charted and linked from the health dashboard. Run T037–T039 and quickstart §5.

---

## Phase 6: User Story 4 - Operator sees which commands are used most (Priority: P4)

**Goal**: Command calls are counted by key and ranked in the dashboard.

**Independent Test**: Send TestBot registered commands, `/start`, `/changelanguage` and made-up commands; the command usage panel ranks them, groups unknown ones as `(unknown)` and never shows their names (quickstart §3).

### Tests for User Story 4

- [X] T043 [P] [US4] Add tests to `tests/TestBot.UnitTests/Controllers/UpdateControllerTests.cs`: processed `/start`, `/changelanguage`, a TestBot command and `/foo` add to `botfarm.commands` with keys `start`, `changelanguage`, the command's key and `(unknown)`; TestBot's GetLastGif command addressed as `<command>@<TestBot's username>` counts under its key without the `@` suffix; a command for another bot adds nothing to `botfarm.commands`; no measurement carries the text `foo`
- [X] T044 [P] [US4] Extend `tests/BotFarm.UnitTests/Telemetry/DashboardDefinitionTests.cs`: panel 4 "Command usage" is a bar gauge over `botfarm_commands_total` grouped by `command`, sorted highest first, over the dashboard's range

### Implementation for User Story 4

- [X] T045 [US4] Add panel 4 "Command usage" (bar gauge, instant query `sort_desc(sum by (command) (increase(botfarm_commands_total{bot="$bot"}[$__range])))`, legend `/{{command}}`) between Handling time and App memory in `monitoring/grafana/dashboards/botfarm-dashboard.json` (depends on T041)

**Checkpoint**: All five panels exist in FR-016 order. Run T043–T044.

---

## Phase 7: User Story 5 - Developer sees metrics locally (Priority: P5)

**Goal**: A developer starts the local stack with one command and sees the dashboard with no account.

**Independent Test**: From a clean checkout, start the stack with the documented command, run the app, send TestBot a message and see it on `http://localhost:3000/d/botfarm/botfarm` within 5 minutes; with the stack stopped the app keeps working and logs one warning; clearing the endpoint stops export and warnings (quickstart §2).

### Tests for User Story 5

- [X] T046 [P] [US5] Add `tests/BotFarm.UnitTests/Telemetry/LocalStackDefinitionTests.cs` reading `monitoring/docker-compose.yml` and `monitoring/grafana/dashboards.yaml` as text: the image tag is pinned (not `latest`), every published port is bound to `127.0.0.1`, the dashboard file is mounted inside the provider's path, and `appsettings.Development.json` points `Telemetry:OtlpEndpoint` at the published OTLP/HTTP port and `Telemetry:DashboardUrl` at uid `botfarm`; `GF_AUTH_ANONYMOUS_ENABLED` is `true`; `BotFarm/appsettings.json` has empty `Telemetry:OtlpEndpoint` and `Telemetry:DashboardUrl`

### Implementation for User Story 5

- [X] T047 [US5] Add a "Metrics" section to `README.md` for developers: the one command to start the local stack, the dashboard address, how to stop it, and how to turn export off by clearing `Telemetry:OtlpEndpoint` in user settings

**Checkpoint**: The local workflow is documented and checked. Run T046 and quickstart §2.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Production setup, documentation and final validation.

- [X] T048 [P] Add Grafana Cloud setup to the "Metrics" section of `README.md`: where to find the OTLP endpoint, instance ID and token; the `OTLP_ENDPOINT`, `OTLP_HEADERS` and `GRAFANA_DASHBOARD_URL` variables for `docker-compose.yml`; checking the metric names in Explore before importing `monitoring/grafana/dashboards/botfarm-dashboard.json`; that export is off when no endpoint is set; that the token belongs in the environment, never in a tracked file
- [X] T049 [P] Build and test `templates/BotFarm.Bot/BotTemplate/BotTemplate.csproj` and `templates/BotFarm.Bot/tests/BotTemplate.UnitTests`, plus `TestBot` and `tests/TestBot.UnitTests`, with no changes to bot code, proving no bot needs to opt in; if they fail, fix Core only
- [X] T050 Build the solution with 0 warnings and run all five test projects (`tests/BotFarm.Core.UnitTests`, `tests/BotFarm.UnitTests`, `tests/BotFarm.Shared.UnitTests`, `tests/TestBot.UnitTests`, `tests/BotFarm.IntegrationTests`), reporting any failure or skip
- [ ] T051 Run quickstart §2–§5 and §7 against the local stack and a live TestBot, and §6 against Grafana Cloud (needs a real bot token, tunnel and Grafana Cloud stack; done by the operator)

---

## Phase 9: Bot events on the dashboard (FR-021, FR-022)

**Purpose**: Mark backups and restores on the charts, so changes in the data can be lined up with them.

- [X] T052 [P] Add `tests/BotFarm.Core.UnitTests/Telemetry/BotEventMetricsTests.cs`: each `BotEvent` adds 1 tagged with the bot and its tag value; `RecordZeros` adds 0 for every event
- [X] T053 Create `BotFarm.Core/Telemetry/BotEventMetrics.cs` (`BotEvent`, `BotEventMetrics`, tag values) per contracts §2b and register it in `BotFarm.Core/Extensions/ServiceCollectionExtensions.cs`
- [X] T054 Record backup and restore results in `BotFarm.Core/Services/MongoDbBackupService.cs`, not counting a cancelled backup; assert the recorded events in `tests/BotFarm.Core.UnitTests/Services/MongoDbBackupServiceTests.cs`
- [X] T055 Record event zeros for every registered bot in `BotFarm/Telemetry/TelemetryStartupService.cs`, covered by `tests/BotFarm.UnitTests/Telemetry/TelemetryStartupServiceTests.cs`
- [X] T056 Add the four event annotations to `monitoring/grafana/dashboards/botfarm-dashboard.json` per data-model §3, and check in `tests/BotFarm.UnitTests/Telemetry/DashboardDefinitionTests.cs` that every `BotEvent` has an enabled annotation for the selected bot
- [X] T057 Add `BotEnabled`, `BotDisabled` and `PendingUpdatesDropped` to `BotEvent` and their cases to `tests/BotFarm.Core.UnitTests/Telemetry/BotEventMetricsTests.cs`
- [X] T058 Record applied on/off changes and dropped pending updates in `BotFarm.Core/Services/BotControlCoordinator.cs`; assert them in `tests/BotFarm.Core.UnitTests/Services/BotControlCoordinatorTests.cs` (turning on, off, repeating a state, a failed enable, a retry, a failed drop); add `TestMetrics.BotEvents()` in `tests/BotFarm.TestKit/TestMetrics.cs` for the other tests that build a coordinator
- [X] T059 Add the "Bot turned on", "Bot turned off", "Pending updates dropped" and "App restarted" annotations to `monitoring/grafana/dashboards/botfarm-dashboard.json` per data-model §3, with a test for the restart query

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none.
- **Foundational (Phase 2)**: needs Setup; blocks every story.
- **US1 (Phase 3)**: needs Foundational. MVP.
- **US2 (Phase 4)**: needs US1's `Post` recording (T027) and dashboard file (T029).
- **US3 (Phase 5)**: needs US1's startup service (T028), links (T030–T031) and the dashboard panels before it (T036).
- **US4 (Phase 6)**: needs Foundational's classification and US1's recording; its panel goes after US3's (T041).
- **US5 (Phase 7)**: needs Foundational's local stack (T018–T019); can run any time after Phase 2.
- **Polish (Phase 8)**: needs all stories.

### Within Each Story

- Tests first, failing, then implementation.
- The dashboard file is edited by one task at a time in panel order: T029 → T036 → T041 → T045.
- `UpdateControllerBase.cs` is edited by T027, then T035.
- `UpdateControllerTests.cs` is edited by T020, T032 and T043 in that order.

### Parallel Opportunities

- Phase 1: T002, T003, T004 after T001.
- Phase 2: tests T005–T009 together; T010, T014, T015, T017, T018 together; then T011 → T012, T013 → T016, T019.
- US1: tests T020–T026 together; T028, T029, T030 together after T027.
- US3: T041 and T042 together after T040.
- US5 can run beside US2–US4.

## Parallel Example: User Story 1

```text
T020 UpdateControllerTests (throughput)
T021 WebhookPipelineTests (throughput)
T022 TelemetryExportTests
T023 TelemetryStartupServiceTests
T024 DashboardDefinitionTests
T025 MetricsLinksTests
T026 MetricsLinkRenderTests + BotDashboardRenderTests
```

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 and Phase 2.
2. Phase 3 (US1).
3. Stop and validate with quickstart §2–§3: throughput reaches the local dashboard and the bot link opens it.

### Incremental Delivery

1. US1: throughput and the bot link.
2. US2: errors and handling time.
3. US3: memory and the health link.
4. US4: command usage.
5. US5: local workflow documented and checked.
6. Polish: Grafana Cloud documentation and full validation.
