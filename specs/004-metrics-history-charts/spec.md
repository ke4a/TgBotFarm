# Feature Specification: Metrics History and Charts

**Feature Branch**: `[master]`

**Created**: 2026-11-21

**Status**: Draft

**Input**: User description: "Give the admin a view of how each bot and the app behave over time: update throughput by kind, errors and failures, p50/p95 handling time against the webhook deadline, ranked command usage, and app memory against the memory warning threshold. Export the measurements to an external monitoring service and chart them there: Grafana Cloud Free in production, and a local Grafana stack in Docker for development."

**Context**: The admin dashboards show only point-in-time values today: chat count, chats by language, database size, current memory use, the webhook's pending update count and its last error. Nothing is kept over time, so the operator cannot see trends, error spikes or slow memory leaks. This feature makes the app report a small set of measurements to an external monitoring service, which stores them and draws the charts. The app keeps no metrics history of its own. Production reports to a hosted service; local development reports to the same kind of service running in a container on the developer's machine.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator sees a bot's update throughput over time (Priority: P1)

An operator opens a bot's admin dashboard, follows its "Metrics" link, and lands on the monitoring dashboard filtered to that bot. A chart shows how many updates the bot handled over time, split into commands, callback queries and other updates. They widen the range from the last 24 hours to the last 7 days and see the weekly pattern.

**Why this priority**: Throughput is the base measurement every other chart is read against, and reporting it establishes the whole path (measuring, exporting, storing, charting). It is a usable MVP on its own.

**Independent Test**: Send a known mix of commands, button presses and plain messages to TestBot with the local monitoring stack running, wait for the next export, and confirm the counter totals rose by exactly those counts by kind and the dashboard shows them.

**Acceptance Scenarios**:

1. **Given** TestBot received 5 commands, 3 callback queries and 7 other updates, **When** the operator views the throughput chart after the next export, **Then** the chart shows those three kinds for that period, the period's totals are exactly 5 commands, 3 callback queries and 7 other updates, and hovering shows each kind's value and the time.
2. **Given** a bot is enabled but received nothing, **When** the operator views its throughput, **Then** the chart shows zero, not "no data".
3. **Given** the app was stopped for 20 minutes, **When** the operator views that period, **Then** those 20 minutes show no data, distinct from periods in which the app ran and the bot received nothing.
4. **Given** the app restarted, **When** the operator views throughput across the restart, **Then** counts before and after the restart are both shown and the restart does not appear as a negative value or a spike.
5. **Given** no monitoring dashboard address is configured, **When** the operator opens a bot's admin dashboard, **Then** no "Metrics" link is shown.
6. **Given** an operator installs the app with the shipped production configuration and sets no export endpoint, **When** the app runs, **Then** it sends no telemetry, logs no export warnings, and its dashboards show the same point-in-time values as without the feature.

---

### User Story 2 - Operator spots error spikes and slow handling (Priority: P2)

An operator notices users complaining that a bot is slow. In the monitoring dashboard they look at the errors and handling time charts for the last 24 hours and see that failures and the 95th percentile handling time both rose sharply at 14:00, with the webhook deadline drawn for comparison.

**Why this priority**: Spotting and dating problems is the main reason for keeping history, but it builds on the reporting from Story 1.

**Independent Test**: Make TestBot fail a known number of updates (permanent and temporary errors), send a duplicate delivery and a request with a wrong secret, and slow its handling for a period; confirm each count and the percentile rise appear in the right series.

**Acceptance Scenarios**:

1. **Given** 2 updates failed with a permanent error and 1 update failed with a temporary error and was later dropped after its last retry, **When** the operator views the errors chart for that period, **Then** that period's totals are 7 failed (2 permanent failures plus 5 failed attempts of the temporary one), 4 retried (the attempts answered with a request to deliver again) and 1 dropped.
2. **Given** Telegram delivered an already accepted update again, **When** the operator views the errors chart, **Then** it is shown in a separate "Duplicates skipped" series that is not added to the failure total.
3. **Given** the webhook answered requests with an invalid secret or while the bot was not ready, **When** the operator views the errors chart, **Then** those requests appear in a "Webhook rejected" series.
4. **Given** a bot's handling slowed from about 50 ms to about 3 s, **When** the operator views the handling time chart, **Then** the p50 and p95 lines rise accordingly, and hovering shows both values in seconds.
5. **Given** any range, **When** the operator views the handling time chart, **Then** the 60 s webhook deadline is drawn as a labelled reference line on the same axis.
6. **Given** the nightly backup of a bot ran at 05:00 and an operator restored a backup at 14:10, **When** the operator views that bot's charts for the last 24 hours, **Then** each chart marks "Backup created" near 05:00 and "Backup restored" near 14:10; a failed backup or restore is marked "Backup failed" or "Restore failed" in a warning colour. Turning the bot on or off, dropping its pending updates and restarting the app are marked "Bot turned on", "Bot turned off", "Pending updates dropped" and "App restarted".

---

### User Story 3 - Operator watches app memory for slow leaks (Priority: P3)

An operator opens the monitoring dashboard's app section, from the health dashboard's "Metrics" link, and sees working set and GC heap size over the last 7 days with the memory warning threshold as a reference line. A steady climb in GC heap size across days shows a leak long before the threshold warning fires.

**Why this priority**: Memory history serves the whole app rather than one bot, and the existing threshold warning already covers the worst case, so it comes after the per-bot charts.

**Independent Test**: Run the app with the local monitoring stack, change the configured threshold and restart, and confirm the chart shows both memory series and the threshold as it was configured at each point in time.

**Acceptance Scenarios**:

1. **Given** the app has been running for an hour, **When** the operator opens the memory chart, **Then** it shows working set and GC heap size in megabytes and the threshold line.
2. **Given** the threshold was raised from 400 MB to 500 MB yesterday, **When** the operator views the last 7 days, **Then** the threshold line steps from 400 to 500 at the time of the change.

---

### User Story 4 - Operator sees which commands are used most (Priority: P4)

An operator views a bot's command usage for the last 7 days and sees the bot's commands ranked by number of calls, including /start and /changelanguage, with all unrecognized commands grouped into one entry.

**Why this priority**: Useful for product decisions about the bot, but not for keeping it running.

**Independent Test**: Send TestBot each registered command a known number of times, plus /start, /changelanguage and several made-up commands; confirm the ranking and counts.

**Acceptance Scenarios**:

1. **Given** TestBot received /start 10 times, a registered command 25 times and /changelanguage 3 times in the range, **When** the operator views command usage, **Then** the entries are ranked by their totals for the range, 25, 10, 3, with each command's name.
2. **Given** users sent /foo and /bar, which no handler is registered for, **When** the operator views command usage, **Then** both are counted in one "Unknown command" entry and the names /foo and /bar are not reported.
3. **Given** a command was sent as /help@ThisBot in a group, **When** it is counted, **Then** it is counted as /help; commands addressed to another bot are not counted.

---

### User Story 5 - Developer sees metrics locally (Priority: P5)

A developer working on a bot starts the local monitoring stack with one command, runs the app in development as usual, and sees their bot's charts in a local dashboard, with no account or credentials.

**Why this priority**: Needed to build and check the dashboards and measurements, but production use does not depend on it.

**Independent Test**: From a clean checkout with the normal development setup, start the local stack with the documented command, run the app, send TestBot a message, and confirm it appears on the local dashboard.

**Acceptance Scenarios**:

1. **Given** the local stack is running, **When** the app runs in development with its default settings, **Then** its measurements reach the local stack without further configuration.
2. **Given** the local stack has just started, **When** the developer opens it, **Then** the BotFarm dashboard is already there, loaded from the dashboard definition in the repository.
3. **Given** the local stack is not running, **When** the app runs in development, **Then** the app works normally and export failures are only logged.
4. **Given** a developer does not want to run the local stack, **When** they clear the export endpoint in their own development settings, **Then** export is off and no export warnings are logged.

---

### Edge Cases

- **Monitoring service unreachable**: Update handling and webhook replies continue unchanged. Measurements for the time the service is unreachable may be lost; export resumes by itself once the service is reachable again, and a warning is logged at most once per outage.
- **Crash**: An unexpected process exit loses at most the measurements gathered since the last export (FR-012).
- **Restart**: Counters start again from zero after a restart; the monitoring service treats this as a reset, so rates and totals stay correct across it.
- **Bot disabled or paused**: While a bot is disabled or its updates are paused (for example during a restore), its throughput shows zero; webhook requests answered "not ready" in that time are counted as rejected.
- **Retried updates**: An update that failed with a temporary error and is delivered again counts once per delivery in throughput and handling time, because each delivery is processed (FR-002).
- **Backup and restore**: Backups and restores are unaffected, because the app stores no measurements in its databases.
- **Configuration change of the threshold**: The memory chart shows the threshold reported at each point in time, not today's value applied to the past.
- **Bot renamed or removed**: A removed bot stops reporting; its history stays in the monitoring service until the service's retention removes it.
- **Hosted plan limits**: If the number of distinct series grows past the hosted plan's limit, the service may drop data; the labelling rules in FR-008 keep the count well below it.
- **Time zones**: The monitoring dashboard shows times in the viewer's time zone.

## Requirements *(mandatory)*

### Functional Requirements

#### What is measured

- **FR-001**: The system MUST classify every update a bot processes as exactly one kind: **command** (a message holding a bot command for this bot, in a private chat or addressed to this bot by username elsewhere), **callback query**, or **other** (every other update).
- **FR-002**: Throughput MUST count each webhook delivery whose update is processed, once per delivery, by bot and kind; deliveries skipped as duplicates and requests rejected before processing are not counted in throughput.
- **FR-003**: Handling time MUST be measured for each processed delivery, from just after the duplicate check until processing ends (successfully or with an error), and reported as a distribution that can tell apart at least 10 ms, 50 ms, 100 ms, 250 ms, 500 ms, 1 s, 2.5 s, 5 s, 10 s, 30 s and 60 s, so the dashboard can show p50 and p95.
- **FR-004**: The system MUST count, per bot, these outcomes, each independently:
  - **Failed**: deliveries whose processing ended with any error, temporary or permanent;
  - **Retried**: deliveries answered with a request to deliver again after a temporary error;
  - **Dropped**: updates given up after the last allowed temporary failure;
  - **Duplicates skipped**: deliveries of an already accepted update, acknowledged without processing;
  - **Webhook rejected**: requests answered with an invalid-secret or not-ready response.
- **FR-005**: Duplicates skipped MUST NOT be included in any failure total or error panel; they are shown as delivery information only.
- **FR-006**: Command usage MUST count, per bot, every command routed to the bot: built-in commands (/start and /changelanguage), registered commands, and all other commands grouped into one "Unknown command" entry. Command names MUST be reported without the "@botname" suffix and in lower case. Names of unknown commands MUST NOT be reported.
- **FR-007**: The system MUST report the app's working set, GC heap size and the configured memory warning threshold.
- **FR-008**: Reported measurements MUST NOT contain chat ids, user ids, usernames, message text, callback data or any other content from updates. The only labels on bot measurements are the bot's name, the update kind, the outcome and the command name from FR-006, so the number of distinct series stays fixed by the number of bots and commands.
- **FR-009**: Every registered bot's throughput and outcome counts, and its count for each built-in and registered command and for "Unknown command", MUST be reported from startup with zero when nothing happened. An idle bot then shows zero rather than "no data", and the first call after startup is counted.
- **FR-010**: The system MUST also report the platform's built-in runtime and process measurements (such as garbage collection and thread pool use), and MUST NOT report per-request web server measurements that are labelled by route or status code.
- **FR-021**: The system MUST count, per bot, backups created, backups failed, backups restored, restores failed, the bot being turned on or off by an operator (counted once the change is applied, and not when the bot is already in that state) and pending updates dropped by an operator. Each count is reported from startup with zero; a cancelled backup is not counted. The counts carry only the bot's name and the event.

#### Export

- **FR-011**: Measurements MUST be exported over the open standard telemetry protocol supported by both the hosted and the local monitoring service. Export is on only when an endpoint is set:
  - either in the app's own configuration,
  - or through the protocol's standard endpoint environment variable, with the app's own configuration taking precedence when both are set.

  When neither is set, export is off, no exporter or export background work runs, nothing is sent, and the app works as it does without the feature. The shipped production configuration sets no endpoint, so export is off unless the operator sets one; the shipped development configuration points at the local monitoring stack from FR-020.
- **FR-012**: Measurements MUST be exported at least once a minute and on graceful shutdown.
- **FR-013**: The credentials for the hosted service MUST come from secret configuration, MUST NOT be committed to the repository, and MUST NOT appear in logs.
- **FR-014**: Taking a measurement MUST NOT perform storage or network access during update handling. A failure in measuring or exporting MUST NOT change the webhook's response, delay it, or stop update handling; export failures are logged without secrets and at most once per outage.
- **FR-015**: The app MUST NOT store measurements in its own databases, and bot backups and restores MUST be unaffected by this feature.

#### Dashboards

- **FR-016**: The repository MUST contain one monitoring dashboard definition with a bot selector and these panels, in this order: throughput (stacked by kind), errors (one series per outcome in FR-004), handling time (p50 and p95 lines with the 60 s webhook deadline as a labelled reference line), command usage (ranked, highest first) and app memory (working set and GC heap size with the threshold as a reference line).
- **FR-017**: Dashboard panels MUST follow these presentation rules: a panel with a single series has no legend; a panel with several series has a legend; no panel has two y-axes; hovering shows the time and each series' value with its unit; axis units are stated; series are readable in both light and dark themes.
- **FR-018**: The dashboard MUST offer at least the ranges 1 hour, 24 hours and 7 days, defaulting to 24 hours.
- **FR-019**: When a monitoring dashboard address is configured, each bot's admin dashboard MUST show a "Metrics" link that opens the monitoring dashboard filtered to that bot, and the health dashboard MUST show a "Metrics" link to the app memory panel. The link texts MUST be available in English, Russian and Ukrainian. When no address is configured, no link is shown.
- **FR-022**: The dashboard MUST mark each event from FR-021 for the selected bot on its time-series panels, and every start of the app, as a named marker placed within the 2-minute interval in which it happened, with failures in a warning colour.

#### Local development

- **FR-020**: The repository MUST provide a local monitoring stack that starts with one documented command, needs no account or credentials, loads the dashboard definition from FR-016 automatically, and receives measurements from the app's default development configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can find the hour in the last 7 days with the most failures for a given bot in under 30 seconds from opening the bot's admin dashboard.
- **SC-002**: With the monitoring service unreachable for 10 minutes, every webhook update is still answered as it would be without the feature, and measurements appear again within 2 minutes of the service becoming reachable.
- **SC-003**: With 5 bots of 10 registered commands each, the app reports fewer than 2,000 distinct series.
- **SC-004**: A rise of the 95th percentile handling time from under 100 ms to over 1 s is visible on the handling time panel.
- **SC-005**: A memory increase of 50 MB or more sustained over 3 days is visible as a rising line on the 7-day memory panel.
- **SC-006**: From a clean checkout with the normal development setup, a developer sees TestBot's measurements on the local dashboard within 5 minutes, using only the documented command.
- **SC-007**: Bot backup archives and restore behaviour are the same as without the feature.
- **SC-008**: With export off, the app makes no network connections for telemetry, and webhook response times are the same as without the feature.

## Assumptions

- Monitoring is optional. An operator who sets up no monitoring service keeps the existing point-in-time dashboards and has no metrics history.
- Production uses Grafana Cloud's free plan, which accepts OpenTelemetry (OTLP) data and currently allows 10,000 active series with 14 days of retention. Retention is whatever the hosted plan provides; the app does not control it.
- Local development uses Grafana's single-container OpenTelemetry stack, which bundles a collector, metrics storage and Grafana, started through Docker.
- Per-interval bars and lines are estimated by the monitoring service from counters exported once a minute, so a single bar can differ slightly from the exact count. Exact totals are read from the counters themselves.
- Memory sizes use the same megabytes as `HealthChecks:Memory:ThresholdMb`, that is 1,048,576 bytes.
- Telegram's webhook response limit is treated as 60 seconds, the value the existing Telegram request timeout is set to stay under.
- The monitoring dashboard is in English; only the admin dashboard links are localized.
- Access to the hosted dashboard is controlled by the hosted service's own accounts, separate from the admin UI's sign-in.
- Measurements hold no chat or user data (FR-008), so sending them to a hosted service is acceptable.
- The existing health checks, their publishing schedule and the memory warning are unchanged.
- Webhook-rejected requests are counted per bot from the bot's webhook endpoint; requests to unknown endpoints are not counted.
- The duplicate-detection and retry records stay in memory as today; only counts of their outcomes are reported.
- Telegram's own reported webhook errors are not reported; the admin dashboard already shows the last one.
- Out of scope: charts inside the admin UI, metrics history stored by the app, alert rules (the monitoring service supports them, but none are set up), exporting logs or traces, metrics defined by bots themselves, and per-chat or per-user analytics.
