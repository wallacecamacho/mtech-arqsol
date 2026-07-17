/**
 * k6 Load Test — CashFlow Gateway
 *
 * Validates RNF: 50 req/s sustained throughput with ≤5% error rate.
 *
 * Prerequisites:
 *   - k6 installed (https://k6.io/docs/getting-started/installation/)
 *   - Stack running: docker compose up -d
 *   - Demo user: merchant1 / Demo@1234
 *
 * Run:
 *   k6 run tests/load/k6-load.js
 *
 * Override gateway URL:
 *   k6 run -e BASE_URL=http://localhost:8000 tests/load/k6-load.js
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// ── Custom metrics ────────────────────────────────────────────────────────────
const errorRate = new Rate('cashflow_errors');
const createEntryDuration = new Trend('cashflow_create_entry_ms', true);
const getConsolidatedDuration = new Trend('cashflow_get_consolidated_ms', true);

// ── Options ───────────────────────────────────────────────────────────────────
export const options = {
  stages: [
    { duration: '10s', target: 50 },  // ramp up to 50 VUs
    { duration: '30s', target: 50 },  // sustain 50 VUs → ~50 req/s
    { duration: '10s', target: 0 },   // ramp down
  ],
  thresholds: {
    // Core RNF requirements
    http_req_failed:          ['rate<0.05'],          // ≤5% error rate
    http_req_duration:        ['p(95)<500'],          // 95th percentile <500ms
    cashflow_errors:          ['rate<0.05'],
    cashflow_create_entry_ms: ['p(95)<500'],
    cashflow_get_consolidated_ms: ['p(95)<500'],
  },
};

// ── Setup: obtain JWT token ───────────────────────────────────────────────────
const BASE_URL = __ENV.BASE_URL || 'http://localhost:8000';

export function setup() {
  const res = http.post(
    `${BASE_URL}/api/auth/token`,
    JSON.stringify({ username: 'merchant1', password: 'Demo@1234' }),
    { headers: { 'Content-Type': 'application/json' } }
  );

  if (res.status !== 200) {
    throw new Error(`Auth failed (${res.status}): ${res.body}`);
  }

  const body = JSON.parse(res.body);
  return { token: body.token };
}

// ── Main scenario ─────────────────────────────────────────────────────────────
export default function (data) {
  const headers = {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${data.token}`,
  };

  const today = new Date().toISOString().split('T')[0];

  // --- POST /api/entries ---
  const entryPayload = JSON.stringify({
    amount: 100.50,
    currency: 'BRL',
    type: 0, // Credit
    description: 'k6 load test entry',
    entryDate: today,
  });

  const createRes = http.post(`${BASE_URL}/api/entries`, entryPayload, { headers });
  createEntryDuration.add(createRes.timings.duration);

  const createOk = check(createRes, {
    'create entry: status 201': (r) => r.status === 201,
    'create entry: has id':     (r) => {
      try { return !!JSON.parse(r.body).id; } catch { return false; }
    },
  });
  errorRate.add(!createOk);

  sleep(0.5);

  // --- GET /api/consolidated/{date} ---
  const consolidatedRes = http.get(`${BASE_URL}/api/consolidated/${today}`, { headers });
  getConsolidatedDuration.add(consolidatedRes.timings.duration);

  const consolidatedOk = check(consolidatedRes, {
    'get consolidated: status 2xx': (r) => r.status >= 200 && r.status < 300,
  });
  errorRate.add(!consolidatedOk);

  sleep(0.5);
}
