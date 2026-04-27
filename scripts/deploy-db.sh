#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DATABASE_URL_VALUE="${1:-${DATABASE_URL:-}}"

if [[ -z "$DATABASE_URL_VALUE" ]]; then
  echo "Usage: ./scripts/deploy-db.sh '<postgresql-connection-string>'"
  echo "Or set DATABASE_URL in the environment."
  exit 1
fi

normalize_connection_string() {
  local value="$1"

  if [[ "$value" != postgresql://* && "$value" != postgres://* ]]; then
    printf '%s' "$value"
    return 0
  fi

  local without_scheme="${value#postgresql://}"
  without_scheme="${without_scheme#postgres://}"

  local main_part="$without_scheme"
  local query_part=""
  if [[ "$without_scheme" == *\?* ]]; then
    main_part="${without_scheme%%\?*}"
    query_part="${without_scheme#*\?}"
  fi

  local auth_part="${main_part%%@*}"
  local host_db_part="${main_part#*@}"
  local username="${auth_part%%:*}"
  local password="${auth_part#*:}"
  local host_port_part="${host_db_part%%/*}"
  local database="${host_db_part#*/}"
  local host="${host_port_part%%:*}"
  local port="5432"
  if [[ "$host_port_part" == *:* ]]; then
    port="${host_port_part##*:}"
  fi

  local connection_string="Host=${host};Port=${port};Database=${database};Username=${username};Password=${password}"

  if [[ -n "$query_part" ]]; then
    local IFS='&'
    read -ra params <<< "$query_part"
    for param in "${params[@]}"; do
      local key="${param%%=*}"
      local param_value="${param#*=}"
      local normalized_key
      normalized_key="$(printf '%s' "$key" | tr '[:upper:]' '[:lower:]')"
      case "$normalized_key" in
        sslmode)
          connection_string="${connection_string};SSL Mode=${param_value}"
          ;;
        channel_binding)
          connection_string="${connection_string};Channel Binding=${param_value}"
          ;;
      esac
    done
  fi

  printf '%s' "$connection_string"
}

cd "$ROOT_DIR"

NORMALIZED_CONNECTION_STRING="$(normalize_connection_string "$DATABASE_URL_VALUE")"

ConnectionStrings__SaigonWaterbusDb="$NORMALIZED_CONNECTION_STRING" \
ASPNETCORE_ENVIRONMENT=Production \
dotnet run --project ./src/Web --no-launch-profile -- db:migrate-seed
