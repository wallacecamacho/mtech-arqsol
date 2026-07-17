#!/usr/bin/env bash
# =============================================================================
# scripts/reconcile-outbox.sh
#
# Diagnóstico e recuperação manual do Outbox do Entries Service.
#
# Uso:
#   ./scripts/reconcile-outbox.sh                   # diagnóstico (read-only)
#   ./scripts/reconcile-outbox.sh --restart         # reinicia Entries p/ forçar replay
#   ./scripts/reconcile-outbox.sh --purge-old       # remove mensagens processadas >30 dias
#   ./scripts/reconcile-outbox.sh --help
#
# Requer: docker compose rodando (make up)
# =============================================================================

set -euo pipefail

PG_CONTAINER="${PG_CONTAINER:-cashflow-postgres-1}"
COMPOSE_SERVICE="entries"

RED='\033[0;31m'
YEL='\033[1;33m'
GRN='\033[0;32m'
NC='\033[0m'

usage() {
    grep '^#' "$0" | sed 's/^# \{0,2\}//'
    exit 0
}

psql_entries() {
    docker exec "$PG_CONTAINER" psql -U cashflow cashflow_entries -t -A "$@"
}

# ── Parse args ────────────────────────────────────────────────────────────────
CMD="${1:-diagnose}"
[[ "$CMD" == "--help" ]] && usage

echo -e "\n${GRN}=== CashFlow Outbox Reconciliation ===${NC}"
echo "Container : $PG_CONTAINER"
echo "Timestamp : $(date -u '+%Y-%m-%d %H:%M:%S UTC')"

# ── 1. Summary ────────────────────────────────────────────────────────────────
echo -e "\n${YEL}─── Summary ───────────────────────────────────────────${NC}"
psql_entries -c "
SELECT
  COUNT(*)                                          AS total,
  COUNT(*) FILTER (WHERE processed_at IS NOT NULL)  AS processed,
  COUNT(*) FILTER (WHERE processed_at IS NULL)      AS pending,
  COUNT(*) FILTER (WHERE retry_count > 3)           AS high_retry
FROM outbox_messages;"

# ── 2. Pending messages ───────────────────────────────────────────────────────
PENDING=$(psql_entries -c "SELECT COUNT(*) FROM outbox_messages WHERE processed_at IS NULL;")
if [[ "$PENDING" -gt 0 ]]; then
    echo -e "\n${RED}⚠  $PENDING pending message(s):${NC}"
    psql_entries -c "
    SELECT id, event_type, retry_count, occurred_at,
           LEFT(COALESCE(error,'—'), 80) AS last_error
    FROM outbox_messages
    WHERE processed_at IS NULL
    ORDER BY occurred_at
    LIMIT 20;" | column -t -s '|'
else
    echo -e "\n${GRN}✓  No pending messages.${NC}"
fi

# ── 3. High-retry messages ────────────────────────────────────────────────────
HIGH=$(psql_entries -c "SELECT COUNT(*) FROM outbox_messages WHERE retry_count > 3;")
if [[ "$HIGH" -gt 0 ]]; then
    echo -e "\n${RED}⚠  $HIGH message(s) with retry_count > 3 (potential dead letters):${NC}"
    psql_entries -c "
    SELECT id, event_type, retry_count, occurred_at,
           LEFT(COALESCE(error,'—'), 100) AS last_error
    FROM outbox_messages
    WHERE retry_count > 3
    ORDER BY retry_count DESC
    LIMIT 10;" | column -t -s '|'
fi

# ── Actions ───────────────────────────────────────────────────────────────────
case "$CMD" in
    --restart)
        echo -e "\n${YEL}Restarting '$COMPOSE_SERVICE' service to trigger outbox replay…${NC}"
        docker compose restart "$COMPOSE_SERVICE"
        echo "Monitor: docker compose logs -f $COMPOSE_SERVICE | grep -i outbox"
        ;;
    --purge-old)
        echo -e "\n${YEL}Purging processed messages older than 30 days…${NC}"
        DELETED=$(psql_entries -c "
        WITH deleted AS (
            DELETE FROM outbox_messages
            WHERE processed_at < NOW() - INTERVAL '30 days'
            RETURNING id
        ) SELECT COUNT(*) FROM deleted;")
        echo -e "${GRN}✓  Deleted $DELETED row(s).${NC}"
        ;;
    diagnose|*)
        echo -e "\n${GRN}Diagnóstico concluído. Use --restart ou --purge-old para ações corretivas.${NC}"
        ;;
esac
