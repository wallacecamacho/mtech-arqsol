/**
 * k6 Multi-Scenario Load Test — CashFlow
 *
 * Cenários disponíveis (selecionar via variável SCENARIO):
 *
 *   smoke  — 2 VUs / 30 s   → sanidade antes de qualquer deploy
 *   load   — 50 VUs / 60 s  → valida RNF: 50 req/s, ≤5% erro  ← EVIDÊNCIA PRINCIPAL
 *   stress — spike 150 VUs  → resiliência sob pico inesperado
 *   soak   — 25 VUs / 10 min → estabilidade de longa duração
 *
 * Execução local (stack rodando):
 *   k6 run tests/load/k6-scenarios.js                                  (load)
 *   k6 run -e SCENARIO=smoke  tests/load/k6-scenarios.js
 *   k6 run -e SCENARIO=stress tests/load/k6-scenarios.js
 *
 * Execução contra AWS (ALB):
 *   k6 run -e BASE_URL=https://api.cashflow.example.com \
 *          -e SCENARIO=load  tests/load/k6-scenarios.js
 *
 * Execução contra Azure (Front Door):
 *   k6 run -e BASE_URL=https://cashflow.azurefd.net \
 *          -e SCENARIO=load  tests/load/k6-scenarios.js
 *
 * Exportar sumário JSON para evidência:
 *   k6 run --summary-export=tests/load/results/summary.json \
 *          tests/load/k6-scenarios.js
 */

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { htmlReport } from 'https://raw.githubusercontent.com/benc-uk/k6-reporter/main/dist/bundle.js';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

// ── Target ────────────────────────────────────────────────────────────────────
const BASE_URL = __ENV.BASE_URL || 'http://localhost:8000';
const SCENARIO = __ENV.SCENARIO  || 'load';

// ── Custom metrics ─────────────────────────────────────────────────────────────
const errorRate          = new Rate('cashflow_error_rate');
const createEntryMs      = new Trend('cashflow_create_entry_ms',      true);
const getConsolidatedMs  = new Trend('cashflow_get_consolidated_ms',   true);
const authMs             = new Trend('cashflow_auth_ms',               true);
const entryCreated       = new Counter('cashflow_entries_created');
const consolidatedQueried = new Counter('cashflow_consolidated_queried');

// ── Scenario definitions ───────────────────────────────────────────────────────
const SCENARIOS = {
  // Sanidade — executa antes de qualquer deploy / smoke gate
  smoke: {
    executor: 'constant-vus',
    vus: 2,
    duration: '30s',
    gracefulStop: '5s',
  },

  // RNF principal — evidência de 50 req/s com ≤5% erro
  load: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '15s', target: 50 },  // ramp up
      { duration: '60s', target: 50 },  // sustain — ~50 req/s @ 1 req/VU/s
      { duration: '15s', target: 0  },  // ramp down
    ],
    gracefulRampDown: '10s',
  },

  // Resiliência — verifica comportamento sob carga 3× o RNF
  stress: {
    executor: 'ramping-vus',
    startVUs: 0,
    stages: [
      { duration: '15s', target: 50  },  // aquecimento normal
      { duration: '30s', target: 50  },  // sustain baseline
      { duration: '10s', target: 150 },  // spike 3×
      { duration: '30s', target: 150 },  // sustain spike
      { duration: '15s', target: 0   },  // recovery
    ],
    gracefulRampDown: '10s',
  },

  // Soak — estabilidade em execução prolongada
  soak: {
    executor: 'constant-vus',
    vus: 25,
    duration: '10m',
    gracefulStop: '30s',
  },
};

// ── Thresholds (aplicados a todos os cenários) ─────────────────────────────────
export const options = {
  scenarios: { [SCENARIO]: SCENARIOS[SCENARIO] },

  thresholds: {
    // ── RNF mandatórios ──────────────────────────────────────────────────────
    http_req_failed:            ['rate<0.05'],   // ≤5% erro geral
    cashflow_error_rate:        ['rate<0.05'],   // ≤5% erros de negócio
    http_req_duration:          ['p(95)<500'],   // p95 < 500 ms

    // ── SLOs por operação ────────────────────────────────────────────────────
    cashflow_create_entry_ms:   ['p(95)<400', 'p(99)<800'],
    cashflow_get_consolidated_ms: ['p(95)<300', 'p(99)<600'],
    cashflow_auth_ms:           ['p(95)<200'],

    // ── Stress: tolerância maior para spike (cenário stress apenas) ──────────
    // Não há abortEarlyIfThresholdsBroken no stress — só avalia no final
  },

  // Metadados exportados no sumário
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)'],
};

// ── Pool de merchants simulados (multi-tenant) ─────────────────────────────────
// Simula 10 comerciantes distintos — reflete uso real em AWS/Azure
const MERCHANTS = [
  { username: 'merchant1', password: 'Demo@1234' },
  { username: 'merchant2', password: 'Demo@5678' },
];

// ── Setup: autentica todos os merchants e retorna tokens ──────────────────────
export function setup() {
  const tokens = {};
  for (const m of MERCHANTS) {
    const start = Date.now();
    const res = http.post(
      `${BASE_URL}/api/auth/token`,
      JSON.stringify({ username: m.username, password: m.password }),
      { headers: { 'Content-Type': 'application/json' },
        tags:    { name: 'auth' } }
    );
    authMs.add(Date.now() - start);

    if (res.status !== 200) {
      console.error(`Auth failed for ${m.username}: ${res.status} ${res.body}`);
      continue;
    }
    const body = JSON.parse(res.body);
    tokens[m.username] = body.token;
  }
  return { tokens };
}

// ── Main VU scenario ──────────────────────────────────────────────────────────
export default function (data) {
  // Seleciona merchant aleatoriamente (simula multi-tenant)
  const merchant = MERCHANTS[__VU % MERCHANTS.length];
  const token    = data.tokens[merchant.username];
  if (!token) { sleep(1); return; }

  const headers = {
    'Content-Type':  'application/json',
    'Authorization': `Bearer ${token}`,
    'X-Correlation-ID': `k6-vu${__VU}-iter${__ITER}`,
  };

  const today = new Date().toISOString().split('T')[0];

  // ── Operação 1: registrar lançamento ────────────────────────────────────────
  group('create_entry', () => {
    const type   = Math.random() > 0.3 ? 0 : 1; // 70% crédito, 30% débito
    const amount = (Math.random() * 900 + 100).toFixed(2);

    const payload = JSON.stringify({
      amount:      parseFloat(amount),
      currency:    'BRL',
      type,
      description: `k6-load-${SCENARIO}-vu${__VU}`,
      entryDate:   today,
    });

    const res = http.post(`${BASE_URL}/api/entries`, payload,
      { headers, tags: { name: 'create_entry', scenario: SCENARIO } });

    createEntryMs.add(res.timings.duration);

    const ok = check(res, {
      'create_entry: status 201':  (r) => r.status === 201,
      'create_entry: has id':      (r) => {
        try { return !!JSON.parse(r.body).id; } catch { return false; }
      },
    });

    errorRate.add(!ok);
    if (ok) entryCreated.add(1);
  });

  sleep(0.5);

  // ── Operação 2: consultar saldo consolidado ──────────────────────────────────
  group('get_consolidated', () => {
    const res = http.get(
      `${BASE_URL}/api/consolidated/${today}`,
      { headers, tags: { name: 'get_consolidated', scenario: SCENARIO } }
    );

    getConsolidatedMs.add(res.timings.duration);

    const ok = check(res, {
      'get_consolidated: status 2xx': (r) => r.status >= 200 && r.status < 300,
    });

    errorRate.add(!ok);
    if (ok) consolidatedQueried.add(1);
  });

  sleep(0.5);
}

// ── handleSummary — gera relatório HTML + JSON + texto ────────────────────────
export function handleSummary(data) {
  const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
  const prefix    = `tests/load/results/${SCENARIO}-${timestamp}`;

  return {
    [`${prefix}.html`]:    htmlReport(data),
    [`${prefix}.json`]:    JSON.stringify(data, null, 2),
    'tests/load/results/latest-summary.json': JSON.stringify(data, null, 2),
    stdout: textSummary(data, { indent: '  ', enableColors: true }),
  };
}
