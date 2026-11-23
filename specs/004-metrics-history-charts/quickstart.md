# Quickstart: Validating Metrics History and Charts

This guide shows the feature working end to end. Instruments and configuration are in [data-model.md](data-model.md), and interfaces are in [contracts/metrics-contracts.md](contracts/metrics-contracts.md).

## Prerequisites

- .NET 10 SDK.
- Docker, for the local monitoring stack and the integration tests.
- TestBot enabled in `BotFarm/appsettings.Development.json` with a reachable webhook (Dev Tunnel, ngrok or LocalTunnel), as for normal local development.
- For section 6 only: a Grafana Cloud stack and an access token with permission to write metrics.

## 1. Automated checks

Unit tests cover classification, controller outcomes, telemetry registration and precedence, the export-failure log, memory gauges, the Metrics links and the dashboard file.

```bash
dotnet test tests/BotFarm.Core.UnitTests
```

```bash
dotnet test tests/BotFarm.UnitTests
```

```bash
dotnet test tests/BotFarm.Shared.UnitTests
```

```bash
dotnet test tests/TestBot.UnitTests
```

Integration tests need Docker. They check that a webhook request produces the expected measurements, and that export reaches a fake OTLP endpoint at `/v1/metrics` with the configured header.

```bash
dotnet test tests/BotFarm.IntegrationTests
```

**Expected**: all tests pass.

## 2. Local stack (US5)

Start the stack:

```bash
docker compose -f monitoring/docker-compose.yml up -d
```

Run the app in development as usual, then open `http://localhost:3000/d/botfarm/botfarm`.

**Expected**:
- No sign-in is asked for.
- The BotFarm dashboard is there, and TestBot appears in the bot selector within about a minute.
- Throughput and errors show zero, not "No data".

Stop the stack while the app keeps running.

**Expected**: the app keeps answering webhooks. The log shows one warning that export failed and, after the stack is started again, one line that export recovered.

## 3. Throughput and command usage (US1, US4)

1. In Grafana's **Explore**, run `sum by (kind) (botfarm_updates_total{bot="TestBot"})` and note the values.
2. Send TestBot 5 commands (including `/start` and `/changelanguage`), press 3 inline buttons and send 7 plain messages. Also send `/foo`.
3. Wait about a minute, run the query again, and refresh the dashboard with the range **Last 1 hour**.

**Expected**:
- The query's values rose by exactly 6 commands, 3 callback queries and 7 other updates.
- The throughput bars show about the same numbers; they are estimates per interval.
- Command usage lists `/start`, `/changelanguage` and `(unknown)` with their counts, highest first, and each first call is counted.

Restart the app and send a few more updates.

**Expected**: counts before and after the restart both show, and no negative value or spike appears at the restart.

## 4. Errors and handling time (US2)

1. Point TestBot's database at an unreachable host (or stop MongoDB) and send a message. Telegram retries the update until it is dropped.
2. Send a webhook request with a wrong `X-Telegram-Bot-Api-Secret-Token` header to TestBot's update endpoint.
3. Restore MongoDB and view the errors panel.

**Expected**:
- In Explore, `sum by (outcome) (botfarm_update_outcomes_total{bot="TestBot"})` rose by exactly the attempts in US2 scenario 1, and the errors panel shows about the same.
- The wrong-secret request appears under rejected.
- The handling time panel shows p50, p95 and a dashed "Webhook deadline" line at 60 s.

## 5. Memory and admin links (US3, FR-019)

1. On the admin health dashboard, follow **Metrics**.
   - **Expected**: Grafana opens on the App memory panel with working set, GC heap and a dashed threshold at `HealthChecks:Memory:ThresholdMb`.
2. Raise the threshold, restart, and wait a few minutes.
   - **Expected**: the threshold line steps up at the restart.
3. On TestBot's admin dashboard, follow **Metrics**.
   - **Expected**: the dashboard opens with TestBot selected.
4. Switch the admin UI between English, Russian and Ukrainian.
   - **Expected**: the link text is translated.

## 5a. Event markers (FR-021, FR-022)

1. On TestBot's admin dashboard, create a backup, then restore it.
2. Wait about two minutes and refresh the Grafana dashboard with TestBot selected.

**Expected**: the charts show a green "Backup created" marker and a blue "Backup restored" marker at those times. Hovering a marker shows its name.

3. Turn TestBot off and on again, drop its pending updates, and restart the app.

**Expected**: within about two minutes, "Bot turned off", "Bot turned on", "Pending updates dropped" and "App restarted" markers appear. Turning a bot off that is already off adds no marker.

## 6. Grafana Cloud (production)

1. In the Grafana Cloud stack, open **Connections → OpenTelemetry (OTLP)**. Note the endpoint, the instance ID and a token.
2. Run the app with:
   - `Telemetry__OtlpEndpoint` set to the endpoint (ending in `/otlp`);
   - `Telemetry__OtlpHeaders` set to `Authorization=Basic <base64 of instanceID:token>`.
3. Within two minutes, in **Explore**, query `botfarm_updates_total`.
   - **Expected**: series labelled `bot` and `kind` exist. If the names differ (for example dots are kept), the stack's OTLP name translation differs from research R10 and the dashboard queries must be adjusted before import.
4. Import `monitoring/grafana/dashboards/botfarm-dashboard.json` through **Dashboards → New → Import** and choose the stack's Prometheus datasource.
5. Set `Telemetry__DashboardUrl` to the imported dashboard's URL and restart.
   - **Expected**: the admin Metrics links open the hosted dashboard.
6. After a day, check the stack's usage page.
   - **Expected**: active series are under 2,000 (SC-003).

## 7. Export off (FR-011, SC-008)

Run the app with the production `appsettings.json`, with no `Telemetry__OtlpEndpoint` and no `OTEL_EXPORTER_OTLP_ENDPOINT`.

**Expected**:
- The log has no telemetry lines.
- No outbound telemetry connections are made (check with the container's network view or `lsof -i`).
- No Metrics links appear on the admin dashboards.
