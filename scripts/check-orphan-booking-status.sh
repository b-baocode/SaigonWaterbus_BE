#!/usr/bin/env bash
#
# Pre-deploy safety check: đảm bảo DB không có booking nào dùng booking_status đã bị xóa khỏi enum.
#
# Lý do:
#   - Khi xóa `BookingStatus.Refunded` (= 7) khỏi enum, mọi code path đọc enum đều fail.
#   - Mặc dù BE code không bao giờ set value 7, cần verify trước khi deploy để chắc chắn
#     không có row cũ nào (do manual fix, backup/restore, hay edge case) dùng value này.
#
# Usage:
#   ./scripts/check-orphan-booking-status.sh '<postgresql-connection-string>'
#   hoặc set DATABASE_URL trong env.
#
# Exit codes:
#   0 — không có orphan row, an toàn để deploy.
#   1 — có orphan row, phải xử lý trước khi deploy.
#
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DATABASE_URL_VALUE="${1:-${DATABASE_URL:-}}"

if [[ -z "$DATABASE_URL_VALUE" ]]; then
  echo "Usage: ./scripts/check-orphan-booking-status.sh '<postgresql-connection-string>'"
  echo "Or set DATABASE_URL in the environment."
  exit 1
fi

if ! command -v psql >/dev/null 2>&1; then
  echo "[ERROR] psql not found. Install PostgreSQL client tools."
  exit 1
fi

echo "==> Pre-deploy safety check: orphan BookingStatus values"
echo "    Connection: ${DATABASE_URL_VALUE%%@*}@***"
echo ""

ORPHAN_QUERY=$(cat <<'SQL'
SELECT booking_status, COUNT(*) AS row_count
FROM bookings
WHERE booking_status NOT IN (0, 1, 2, 3, 4, 5, 6)
GROUP BY booking_status
ORDER BY booking_status;
SQL
)

RESULT="$(psql "$DATABASE_URL_VALUE" --no-align --tuples-only --quiet --command "$ORPHAN_QUERY" || true)"

if [[ -z "$RESULT" ]]; then
  echo "[OK] No orphan booking_status rows found."
  echo "     All rows use values: 0=PendingPayment, 1=Confirmed, 2=Cancelled,"
  echo "     3=Expired, 4=Quoted, 5=Completed, 6=PendingQuote"
  echo ""
  echo "[OK] Safe to deploy."
  exit 0
fi

echo "[FAIL] Found orphan booking_status rows in DB:"
echo ""
echo "$RESULT"
echo ""
echo "These rows use booking_status values that were removed from the BookingStatus enum."
echo "Deployment will fail because:"
echo "  - API deserializer cannot parse orphan values to enum."
echo "  - Business logic may misroute bookings."
echo ""
echo "Resolution options:"
echo "  1. Inspect data: psql ... --command \"SELECT id, booking_status, payment_status FROM bookings WHERE booking_status NOT IN (0,1,2,3,4,5,6);\""
echo "  2. Map orphan rows:"
echo "       - booking_status = 7 (legacy 'Refunded'):"
echo "           UPDATE bookings SET booking_status = 2 WHERE booking_status = 7;"
echo "           -- mapping: Cancelled (refund full implies cancellation)"
echo "  3. Or rollback the enum removal (git revert)."
echo ""
exit 1