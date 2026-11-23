# Files

- [Configuration and Operations](deployment.md) - Lists BotFarm's configuration keys and startup validation, the container image and Compose setup with its persistent data volume, liveness and health endpoints with the uptime-monitor API key, scheduled backup and shutdown services, logging, and CI.
- [Metrics and Observability](observability.md) - How BotFarm records per-bot update, command and lifecycle-event metrics plus app memory gauges, exports them over OTLP when an endpoint is configured, charts them in the provisioned Grafana dashboard, and links the admin UI to it.
