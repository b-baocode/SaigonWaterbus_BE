#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DB_HOST="${PGHOST:-localhost}"
DB_PORT="${PGPORT:-5432}"
DB_USER="${PGUSER:-postgres}"
DB_PASSWORD="${PGPASSWORD:-12345}"
VERIFY_DB_NAME="${VERIFY_DB_NAME:-SaigonWaterbusDb_verify_$(date +%s)}"
CONNECTION_STRING="Host=${DB_HOST};Port=${DB_PORT};Database=${VERIFY_DB_NAME};Username=${DB_USER};Password=${DB_PASSWORD};"

cleanup() {
  PGPASSWORD="$DB_PASSWORD" dropdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" --if-exists "$VERIFY_DB_NAME" >/dev/null 2>&1 || true
}

trap cleanup EXIT

cd "$ROOT_DIR"

echo "==> restore"
dotnet restore SaigonWaterbus.slnx

echo "==> build"
dotnet build SaigonWaterbus.slnx -c Debug --no-restore

echo "==> test"
dotnet test SaigonWaterbus.slnx -c Debug --no-restore

echo "==> create temporary database: $VERIFY_DB_NAME"
cleanup
PGPASSWORD="$DB_PASSWORD" createdb -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" "$VERIFY_DB_NAME"

echo "==> apply migrations and seed on temporary database"
ConnectionStrings__SaigonWaterbusDb="$CONNECTION_STRING" \
ASPNETCORE_ENVIRONMENT=Development \
dotnet run --project src/Web --no-build --no-launch-profile -- db:reset-seed

echo "Local verification passed."
