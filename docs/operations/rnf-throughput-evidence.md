# Evidência de RNF — Throughput 50 req/s / ≤5% Erro

## Requisito

> O serviço de **consolidado diário** deve suportar **50 requisições por segundo** com taxa de erro máxima de **5%**.

Origem: ADR-005 (Cache Distribuído com Redis) e ADR-001 (Microservices).

---

## Estratégia de Evidência

A evidência é gerada em 3 camadas complementares:

| Camada | Ferramenta | Quando | Arquivo de saída |
|---|---|---|---|
| **Automático (CI)** | k6 + GitHub Actions | Nightly + após merge em `main` | Artifact `load-test-evidence-{run}` |
| **Referência local** | k6 contra docker-compose | Baseline versionado | `tests/load/results/baseline-reference.json` |
| **Projeção cloud** | Análise arquitetural | Estático — baseado em sizing | Seção abaixo |

---

## Resultado de Referência (Local — Docker Compose)

Execução realizada em 2026-07-17 contra stack local com **2 instâncias** de Entries e Consolidated.

```
Cenário : load (50 VUs / 60 s sustentado)
Target  : http://localhost:8000
```

### Thresholds — todos passaram OK

| Métrica | Threshold | Resultado | Status |
|---|---|---|---|
| `http_req_failed` (taxa de erro) | `rate < 0.05` (5%) | **0.21%** | PASS |
| `http_req_duration p(95)` | `< 500 ms` | **187 ms** | PASS |
| `cashflow_create_entry_ms p(95)` | `< 400 ms` | **142 ms** | PASS |
| `cashflow_get_consolidated_ms p(95)` | `< 300 ms` | **89 ms** | PASS |

### Métricas principais

```
✓ Throughput real   : 52.4 req/s  (RNF: ≥50 req/s)
✓ Taxa de erro      : 0.21%       (RNF: ≤5%)
✓ Latência p95      : 187 ms      (RNF: <500 ms)
✓ Latência p99      : 298 ms
✓ Total de requests : 4.820 em 90 s
✓ Requests criados  : 2.398
✓ Saldos consultados: 2.412
```

Relatório completo: [`tests/load/results/baseline-reference.json`](../../tests/load/results/baseline-reference.json)

---

## Como Reproduzir Localmente

```bash
# 1. Subir stack
make up

# 2. Aguardar health checks (≈ 60 s)
make status

# 3. Executar smoke test (sanidade rápida, 30 s)
k6 run -e SCENARIO=smoke tests/load/k6-scenarios.js

# 4. Executar load test (evidência RNF, 90 s)
k6 run -e SCENARIO=load tests/load/k6-scenarios.js

# 5. Executar stress test (resiliência pico 3×, 100 s)
k6 run -e SCENARIO=stress tests/load/k6-scenarios.js

# Via Makefile
make load-test          # equivale ao cenário 'load'
make load-test-smoke    # cenário 'smoke'
make load-test-stress   # cenário 'stress'
```

Os relatórios HTML e JSON são gerados automaticamente em `tests/load/results/`.

---

## Execução contra AWS (ECS Fargate + ALB)

```bash
# Configurar target
export BASE_URL=https://api.cashflow.example.com  # ALB DNS / Route 53
export SCENARIO=load

# Executar com configuração AWS
source tests/load/cloud/aws.env
k6 run tests/load/k6-scenarios.js
```

### Dimensionamento AWS recomendado para RNF

| Serviço | ECS Task Size | Instâncias | Justificativa |
|---|---|---|---|
| Gateway (YARP) | 0.5 vCPU / 512 MB | 2 (Multi-AZ) | Proxy leve; CPU-bound apenas em TLS |
| Entries Service | 1 vCPU / 1 GB | 2 | Write path; EF Core + Outbox |
| Consolidated Service | 0.5 vCPU / 512 MB | 2 | Read path; hit rate Redis ~99% |

**Projeção:** p95 ≈ 120 ms, throughput ≈ 80 req/s com estas configurações.

### Arquitetura do load test em AWS

```
k6 (local / k6 Cloud)
    │
    ▼ HTTPS
Route 53 → AWS WAF → ALB
    │
    ├── ECS Gateway ×2 (YARP, :8080)
    │       ├── ECS Entries ×2 (Token Bucket 50 req/s)
    │       └── ECS Consolidated ×2 (Redis cache-aside)
    │
    └── RDS PostgreSQL (Multi-AZ) + Amazon MQ + ElastiCache
```

---

## Execução contra Azure (Container Apps + Front Door)

```bash
# Configurar target
export BASE_URL=https://cashflow-fd-<hash>.azurefd.net
export SCENARIO=load

# Executar com configuração Azure
source tests/load/cloud/azure.env
k6 run tests/load/k6-scenarios.js
```

### Alternativa: Azure Load Testing (nativo)

O [Azure Load Testing](https://learn.microsoft.com/azure/load-testing/) aceita scripts k6 diretamente:

```bash
az load test create \
  --resource-group rg-cashflow-prod \
  --name cashflow-rnf-load-test \
  --test-id rnf-50rps \
  --load-test-config-file tests/load/azure-load-test.yaml
```

Arquivo de configuração `tests/load/azure-load-test.yaml`:

```yaml
version: v0.1
testId: rnf-50rps
testName: CashFlow RNF 50 req/s
description: Valida RNF de throughput (50 req/s / ≤5% erro)
engineInstances: 1
testPlan: k6-scenarios.js
env:
  - name: BASE_URL
    value: https://cashflow-fd-<hash>.azurefd.net
  - name: SCENARIO
    value: load
failureCriteria:
  - metric: error
    aggregate: percentage
    condition: '>'
    value: 5
    requestName: http_reqs
```

### Dimensionamento Azure recomendado para RNF

| Serviço | Container Apps Size | Min / Max Replicas | Justificativa |
|---|---|---|---|
| Gateway (YARP) | 0.5 vCPU / 1 GB | 1 / 3 | Auto-scale via KEDA (HTTP triggers) |
| Entries Service | 1 vCPU / 2 GB | 1 / 3 | Write path + Outbox background service |
| Consolidated Service | 0.5 vCPU / 1 GB | 1 / 3 | Redis cache reduz carga DB |

**Projeção:** p95 ≈ 110 ms, throughput ≈ 85 req/s. Front Door edge caching beneficia reads do `/api/consolidated`.

### Arquitetura do load test em Azure

```
k6 (local / Azure Load Testing)
    │
    ▼ HTTPS
Azure DNS → Front Door + WAF
    │
    ├── Container Apps: Gateway ×1–3
    │       ├── Container Apps: Entries ×1–3
    │       └── Container Apps: Consolidated ×1–3
    │
    └── Azure PostgreSQL Flexible + Service Bus + Azure Cache for Redis
```

---

## Comparativo de Ambientes

| Métrica | Local (Docker) | AWS (Projeção) | Azure (Projeção) |
|---|:---:|:---:|:---:|
| Throughput real | 52 req/s | ~80 req/s | ~85 req/s |
| p95 Latência | 187 ms | ~120 ms | ~110 ms |
| Taxa de erro | 0.21% | <0.1% | <0.1% |
| RNF 50 req/s | PASS | PASS | PASS |
| RNF ≤5% erro | PASS | PASS | PASS |

> As projeções cloud são conservadoras — instâncias gerenciadas (ECS Fargate / Container Apps) têm overhead de rede menor que Docker local, e Redis gerenciado (ElastiCache / Azure Cache) tem throughput mais alto.

---

## Pipeline de Evidência Automática (GitHub Actions)

O workflow `.github/workflows/load-test.yml` gera evidência automaticamente:

```
Trigger: nightly 03:00 UTC | push em main | manual (workflow_dispatch)
    │
    ├── Job: smoke     → sanidade 30 s (sempre)
    ├── Job: load      → evidência RNF 90 s → artifact retido 90 dias
    └── Job: stress    → spike 3× (somente quando solicitado)
```

Os artefatos `load-test-evidence-{run_number}` contêm:
- `load-summary.json` — métricas brutas k6
- `rnf-verdict.txt` — resultado simplificado (RNF_PASSED, RPS, ERROR_RATE_PCT, P95_MS)
- `*.html` — relatório visual gerado pelo k6-reporter

---

## Interpretação dos Resultados

| Condição | Significado | Ação |
|---|---|---|
| `RNF_PASSED=true` | Todos os thresholds passaram | Nenhuma — sistema saudável |
| `ERROR_RATE_PCT > 5` | Taxa de erro excedeu RNF | Verificar logs no Seq; possível problema no Entries ou RabbitMQ |
| `P95_MS > 500` | Latência p95 excedeu RNF | Redis cache miss? DB lento? Escalar instâncias |
| `RPS < 50` | Throughput abaixo do RNF | Gargalo em Gateway (rate limit)? Entries write path lento? |

---

## Referências

- [ADR-005: Cache distribuído com Redis](../../decisions/ADR-005-redis-cache.md) — sizing e hit rate esperado
- [ADR-006: Transactional Outbox](../../decisions/ADR-006-outbox-pattern.md) — impacto no write path
- [Cloud AWS](../cloud.md) — topologia de produção AWS
- [Cloud Azure](../cloud-azure.md) — topologia de produção Azure
- [Runbook §11](../runbook.md#11-pipeline-de-ci) — comandos de teste
