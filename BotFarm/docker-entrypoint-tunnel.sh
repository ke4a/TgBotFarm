#!/bin/sh
set -e

# Selects which tunnel client to run: "localtunnel" (default) or "ngrok". Must match the
# app's WebHookUrl setting so BotFarm resolves its webhook from the same tunnel that's
# actually running.
provider="${TUNNEL_PROVIDER:-localtunnel}"

case "$provider" in
  ngrok)
    if [ -z "$NGROK_AUTHTOKEN" ]; then
      echo "NGROK_AUTHTOKEN not set; ngrok tunnel idle. Set NGROK_AUTHTOKEN (and TUNNEL_PROVIDER=ngrok) in .env to use it." >&2
      exec sleep infinity
    fi

    # ngrok's local inspection API (used by BotFarm to discover the public URL) binds to
    # 127.0.0.1 by default, which isn't reachable from other containers; bind it to all
    # interfaces instead via a minimal v3 agent config.
    config_path="/tmp/ngrok.yml"
    printf 'version: "3"\nagent:\n  web_addr: 0.0.0.0:4040\n' > "$config_path"
    exec ngrok http app:5000 --log=stdout --config "$config_path"
    ;;

  localtunnel)
    exec lt --local-host app --port 5000 --subdomain botfarm-webhook
    ;;

  *)
    echo "Unknown TUNNEL_PROVIDER '$provider'. Expected 'localtunnel' or 'ngrok'." >&2
    exit 1
    ;;
esac
