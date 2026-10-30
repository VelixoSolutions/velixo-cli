#!/bin/bash
# Starts both samples behind Caddy: the inventory checker at /, each expense receipts demo under its ERP's path.
# They share the CLI's one sign-in in $VELIXO_CONFIG_DIR. When any of them stops, the script exits, so Fly restarts
# the whole machine.
set -euo pipefail

public="https://${FLY_APP_NAME:-localhost}.fly.dev"
mkdir -p "$VELIXO_CONFIG_DIR" /data/images /data/state/acumatica /data/state/sageintacct

(
    cd /app/inventorychecker
    export HOST=127.0.0.1 PORT=8081 KIOSK_IMAGE_CACHE=/data/images
    export VELIXO_CONNECTION="${ACUMATICA_CONNECTION:-demo}"
    exec node server.mjs
) &

# Each demo reads the settings named for its ERP. The ones shared by both, such as OPENROUTER_API_KEY and the Twilio
# account, reach it unchanged.
expenses() {
    local erp=$1 port=$2 prefix=$3 connection=$4
    local number="${prefix}_TWILIO_FROM_NUMBER" bot="${prefix}_TELEGRAM_BOT_TOKEN"
    (
        cd /app/expensereceipts
        export EXPENSES_ERP="$erp" VELIXO_CONNECTION="$connection" EXPENSES_STATE="/data/state/$erp" EXPENSES_SIMULATOR=off
        export TWILIO_FROM_NUMBER="${!number:-}" TWILIO_WEBHOOK_URL="$public/$erp/sms" TELEGRAM_BOT_TOKEN="${!bot:-}"
        exec /opt/venv/bin/gunicorn --workers 1 --threads 8 --bind "127.0.0.1:$port" 'app:create_app()'
    ) &
}

export ACUMATICA_URL="${ACUMATICA_URL:-https://demo.velixo.com}"
expenses acumatica 8091 ACUMATICA "${ACUMATICA_CONNECTION:-demo}"
expenses sageintacct 8092 SAGEINTACCT "${SAGEINTACCT_CONNECTION:-skyline}"

caddy run --config /app/deploy/Caddyfile --adapter caddyfile &

wait -n
echo "A sample stopped; exiting so Fly restarts the machine." >&2
exit 1
