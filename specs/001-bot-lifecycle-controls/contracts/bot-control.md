# Bot Control Interaction Contract

## Dashboard

The authenticated dashboard presents one row per currently registered bot. Each row shows the
bot's name, desired enabled/disabled state, whether that state is applied, and any pending,
unknown, or failed status. The control is available only to the same authenticated dashboard
users who can access the existing dashboard.

An operator selects an explicit target (`Enable` or `Disable`). The application first persists the
desired state, then applies it within the same request. The UI shows the operation as applying
until the coordinator returns its final status. A persistence failure is shown as an action error;
the UI must not report the requested state as accepted. While the process remains running, the
dashboard retains the unconfirmed target for Retry, which retries the same persistence command
before applying it. The coordinator retries a failed startup or toggle transition up to three times
with bounded delays. If those attempts fail, the UI shows a sanitized error and offers an explicit
retry action.

The coordinator accepts a new target only when the bot is applied. Requests made while the bot is
unknown, pending, or failed are rejected. An explicit retry first persists and applies any
unconfirmed in-process command; otherwise it reloads and reapplies the current saved target. The
switch becomes available for another request once the target is applied. An unconfirmed command
is lost on application restart; only confirmed database state survives.

## Incoming Update Endpoint

The existing webhook route and request format remain unchanged.

| Runtime state | Handler behavior | Delivery response |
|---|---|---|
| Known enabled and applied | Pass the update to bot-specific processing. | Preserve the existing successful response after processing. |
| Known disabled | Do not invoke bot-specific processing. | HTTP 503 so the delivery service may retry after re-enablement. |
| Unknown or enable-pending | Do not invoke bot-specific processing. | HTTP 503 so the delivery service may retry after state becomes known/applied. |
| Disable pending after durable acceptance | Close the local processing gate immediately; do not invoke bot-specific processing. | HTTP 503 while disabled. |

An update already being processed when a disable request is accepted may finish. The contract
does not promise durable queuing by the delivery service; retries are bounded, so a delayed
response is not a guarantee that every update will be delivered after re-enablement.

The application performs one state load and apply pass after the web host starts. If startup
loading or application fails, the bot remains gated; the dashboard can explicitly retry it.
There is no background reconciliation or retry while the application is idle.

## Outbound Messages

Explicit administrator- and system-initiated outbound messages remain permitted for disabled
bots. Bot-specific inbound handlers are not run while disabled or while runtime state is unknown.
