# CashFlow — Sistema de Controle de Fluxo de Caixa

![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet) ![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker) ![RabbitMQ](https://img.shields.io/badge/RabbitMQ-3-FF6600?logo=rabbitmq) ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql) ![Redis](https://img.shields.io/badge/Redis-7-DC382D?logo=redis) ![CI](https://img.shields.io/github/actions/workflow/status/your-org/mtech-arqsol/ci.yml?label=CI)

Solução para o desafio técnico de Arquiteto de Soluções. Sistema event-driven de microservices em .NET 8 para controle de lançamentos financeiros e consolidado diário.

## Sumário

- [Arquitetura](#arquitetura)
- [Quick Start](#quick-start)
- [Executar Testes](#executar-testes)
- [Evidência de RNF (Throughput)](#evidência-de-rnf-throughput)
- [Observabilidade](#observabilidade)
- [Documentação](#documentação)
- [Estrutura do Repositório](#estrutura-do-repositório)
- [Stack Tecnológica](#stack-tecnológica)
- [Decisões Arquiteturais](#decisões-arquiteturais)
- [Matriz de Aderência](#matriz-de-aderência)

## Arquitetura

```
Comerciante
    │
    ▼ HTTPS
┌─────────────────────────────────────────────────────────────────┐
│  API Gateway (YARP)  :8000                                       │
│  • JWT validation + RBAC    • Rate limiting (50 req/s)           │
│  • CORS                     • Security headers                   │
│  • Correlation ID           • Routing                            │
└──────────────┬──────────────────────────┬───────────────────────┘
               │                          │
         /api/entries/*           /api/consolidated/*
               │                          │
               ▼                          ▼
  ┌────────────────────┐      ┌───────────────────────┐
  │  Entries Service   │      │  Consolidated Service  │
  │  (interno)         │      │  (interno)             │
  │  Clean Architecture│      │  Consumer + CQRS       │
  └────────────┬───────┘      └───────────┬────────────┘
               │                          │
    INSERT + Outbox ▼ (atômico)           │ consumes events
       ┌────────────┐   AMQP   ┌─────────┴──────────┐
       │ PostgreSQL │          │     RabbitMQ         │
       │ (entries + │─────────▶│  entry-created queue │
       │  outbox)   │          └─────────┬────────────┘
       └────────────┘                   │ consumed by
                                ┌────────▼──────────┐
                                │ PostgreSQL         │ ◄── Redis Cache
                                │ (consolidated)     │
                                └───────────────────┘
```

**Requisito crítico atendido**: O Entries Service **não depende** do Consolidated. Comunicação é 100% assíncrona via RabbitMQ. Se o Consolidated cair, o Entries continua operando normalmente.

> **Isolamento de portas**: `Entries` e `Consolidated` não expõem portas no host. Todo o tráfego externo passa obrigatoriamente pelo Gateway (JWT + rate limiting no edge).

## Quick Start

### Pré-requisitos

- [Docker](https://docs.docker.com/get-docker/) 24+
- [Docker Compose](https://docs.docker.com/compose/) v2

### 1. Configurar variáveis de ambiente

```bash
cp .env.example .env
```

> **Obrigatório:** edite `.env` e substitua `JWT_SECRET_KEY` por uma chave segura com no mínimo 32 caracteres antes de subir em produção.

### 2. Subir todos os serviços

```bash
make up
# ou: docker compose up --build -d
```

> O `make up` já copia `.env.example` para `.env` automaticamente caso o arquivo ainda não exista.

Aguarde ~60 segundos para os health checks passarem.

### 3. Verificar status

```bash
make status
# ou: docker compose ps
```

### 4. Obter token JWT

> **Credenciais de demonstração** (demo only — não usar em produção):
>
> | Usuário | Senha |
> |---|---|
> | `merchant1` | `Demo@1234` |
> | `merchant2` | `Demo@5678` |

```bash
curl -X POST http://localhost:8000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username": "merchant1", "password": "Demo@1234"}'
```

Resposta:
```json
{
  "token": "eyJ...",
  "merchantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "expiresAt": "2026-07-17T20:00:00Z"
}
```

### 5. Registrar lançamento

```bash
# Substitua <TOKEN> pelo token obtido acima
curl -X POST http://localhost:8000/api/entries \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 150.00,
    "currency": "BRL",
    "type": 1,
    "description": "Venda de produto",
    "entryDate": "2026-07-13"
  }'
```

`type`: `1` = Crédito, `2` = Débito

### 6. Consultar saldo consolidado

```bash
# Saldo do dia atual
curl http://localhost:8000/api/consolidated \
  -H "Authorization: Bearer <TOKEN>"

# Saldo de uma data específica
curl http://localhost:8000/api/consolidated/2026-07-13 \
  -H "Authorization: Bearer <TOKEN>"

# Histórico de saldos (RF5)
curl "http://localhost:8000/api/consolidated/history?from=2026-07-01&to=2026-07-13" \
  -H "Authorization: Bearer <TOKEN>"
```

### 7. Consultar lançamentos com paginação

```bash
# Página 1 (50 por página — padrão)
curl "http://localhost:8000/api/entries?date=2026-07-13" \
  -H "Authorization: Bearer <TOKEN>"

# Página 2 com 10 resultados
curl "http://localhost:8000/api/entries?date=2026-07-13&page=2&pageSize=10" \
  -H "Authorization: Bearer <TOKEN>"
```

Headers de paginação na resposta: `X-Total-Count`, `X-Total-Pages`, `X-Page`, `X-Page-Size`

## Executar Testes

```bash
# Via Makefile (recomendado)
make test

# Apenas testes unitários
dotnet test CashFlow.sln --filter "FullyQualifiedName~UnitTests"

# Testes de performance (requer k6 instalado e stack rodando)
make load-test

# Ou diretamente com o .NET CLI
dotnet test CashFlow.sln --logger "console;verbosity=normal"
```

Testes incluem:
- **Unit Tests**: Domain entities, Value Objects, Command/Query Handlers, Validators
- **Integration Tests**: APIs completas com Testcontainers (PostgreSQL real, RabbitMQ real, Redis real)
- **Load Tests** (`tests/load/k6-load.js`): valida RNF 50 req/s com ≤5% de erro (requer [k6](https://k6.io))

## Evidência de RNF (Throughput)

**RNF**: 50 req/s sustentado, taxa de erro ≤5%, p95 < 500 ms.

### Resultado de Referência (Docker Compose local)

| Métrica | Threshold | Resultado Baseline | Status |
|---|---|---|---|
| Taxa de erro | `< 5%` | **0.21%** | PASS |
| Latência p95 | `< 500 ms` | **187 ms** | PASS |
| Throughput real | `≥ 50 req/s` | **52.4 req/s** | PASS |
| Latência p99 | — | **298 ms** | OK |

> Resultado completo: [`tests/load/results/baseline-reference.json`](tests/load/results/baseline-reference.json)  
> Evidência automática: workflow [`load-test.yml`](.github/workflows/load-test.yml) gera artefato a cada push em `main`.

### Como executar

```bash
make up                  # subir stack

make load-test-smoke     # sanidade rápida (30 s)
make load-test           # evidência RNF  (90 s) — gera HTML + JSON em tests/load/results/
make load-test-stress    # resiliência spike 3× (100 s)

# Contra cloud (após deploy)
make load-test-aws       # ALB AWS: BASE_URL em tests/load/cloud/aws.env
make load-test-azure     # Front Door Azure: BASE_URL em tests/load/cloud/azure.env
```

### Projeção cloud

| Ambiente | Throughput estimado | p95 estimado | Notas |
|---|---|---|---|
| Local (Docker) | 52 req/s | 187 ms | Baseline de referência |
| AWS (ECS Fargate + ALB) | ~80 req/s | ~120 ms | NIC dedicada, ElastiCache <1ms |
| Azure (Container Apps + Front Door) | ~85 req/s | ~110 ms | Front Door edge caching reads |

Doc completo: [docs/operations/rnf-throughput-evidence.md](docs/operations/rnf-throughput-evidence.md)

## Observabilidade

| Painel | URL | Credenciais |
|---|---|---|
| Logs (Seq) | http://localhost:8888 | - |
| Traces (Jaeger) | http://localhost:16686 | - |
| RabbitMQ Management | http://localhost:15672 | cashflow / cashflow_pass |

## Documentação

```
docs/
├── requirements.md              # RF1–RF8 com critérios de aceitação e escopo
├── architecture/
│   ├── context.md           # C4 Context Diagram
│   ├── container.md         # C4 Container Diagram
│   ├── component.md         # C4 Component Diagram + Sequence Diagrams
│   └── domain-capabilities.md  # Bounded contexts, capacidades e priorização
├── security/
│   └── security-design.md   # Threat model, autenticação, RBAC, OWASP
├── decisions/
│   ├── ADR-001-microservices.md
│   ├── ADR-002-async-messaging.md
│   ├── ADR-003-cqrs-mediatr.md
│   ├── ADR-004-jwt-auth.md
│   ├── ADR-005-redis-cache.md
│   └── ADR-006-outbox-pattern.md
└── operations/
    └── runbook.md      # Deploy, monitoramento, escalabilidade, recuperação
```

## Estrutura do Repositório

```
src/
├── services/
│   ├── CashFlow.Entries/         # Serviço de Lançamentos
│   └── CashFlow.Consolidated/    # Serviço de Consolidado
├── shared/
│   ├── CashFlow.SharedKernel/    # Entity, ValueObject, DomainEvent, Result
│   └── CashFlow.EventBus/        # IntegrationEvent, IEventBus, MassTransit impl
└── gateway/
    └── CashFlow.Gateway/         # YARP Gateway + Auth endpoint + RBAC

tests/
├── CashFlow.Entries.UnitTests/
├── CashFlow.Entries.IntegrationTests/
├── CashFlow.Consolidated.UnitTests/
├── CashFlow.Consolidated.IntegrationTests/
└── load/
    └── k6-load.js                # Load test: 50 req/s, thresholds p(95)<500ms

infrastructure/
└── docker/
    └── postgres/init-multiple-dbs.sh
```

## Stack Tecnológica

| Componente | Tecnologia | Justificativa |
|---|---|---|
| Runtime | .NET 8 / ASP.NET Core | LTS, performance, ecossistema |
| Padrão Arquitetural | Clean Architecture + CQRS | Testabilidade, separação de responsabilidades |
| Mensageria | RabbitMQ + MassTransit | Desacoplamento, retry automático, portabilidade |
| Entrega confiável | Transactional Outbox (ADR-006) | Elimina dual-write; evento nunca se perde após commit |
| Banco de Dados | PostgreSQL 16 | ACID, confiabilidade, suporte EF Core excelente |
| Cache | Redis 7 | Performance, suporte distributed cache nativo .NET |
| API Gateway | YARP | Native .NET, flexível, rate limiting integrado |
| Auth | JWT Bearer (HMAC-SHA256) + RBAC | Stateless, policy `merchant-only` em todos os endpoints |
| Observabilidade | Serilog + Seq + OpenTelemetry (Traces + **Metrics**) + Jaeger | Stack completo: logs + traces + métricas (ASP.NET Core + Runtime) |
| Testes | xUnit + FluentAssertions + Moq + Testcontainers | Testes realistas com containers reais |
| Load Test | k6 | Valida RNF 50 req/s com thresholds automatizados |
| CI | GitHub Actions | Build + Unit Tests + Integration Tests por PR |

## Decisões Arquiteturais

Ver [ADRs em docs/decisions/](docs/decisions/).

| ADR | Decisão |
|---|---|
| [ADR-001](docs/decisions/ADR-001-microservices.md) | Arquitetura de Microservices |
| [ADR-002](docs/decisions/ADR-002-async-messaging.md) | Comunicação assíncrona via RabbitMQ |
| [ADR-003](docs/decisions/ADR-003-cqrs-mediatr.md) | CQRS com MediatR e Clean Architecture |
| [ADR-004](docs/decisions/ADR-004-jwt-auth.md) | JWT Bearer + RBAC `merchant-only` |
| [ADR-005](docs/decisions/ADR-005-redis-cache.md) | Cache distribuído com Redis |
| [ADR-006](docs/decisions/ADR-006-outbox-pattern.md) | Transactional Outbox para publicação confiável |

## Segurança

Ver [docs/security/security-design.md](docs/security/security-design.md).

## Operação

Ver [docs/operations/runbook.md](docs/operations/runbook.md).

---

## Matriz de Aderência

Mapeamento de cada critério do enunciado ao estado atual da implementação.

**Legenda:** `OK` Atendido  ·  `~` Parcial  ·  `-` Não atendido

### Arquitetura e Domínio

| Critério | Status | Evidência |
|---|:---:|---|
| Mapeamento de domínios funcionais | OK | [domain-capabilities.md](docs/architecture/domain-capabilities.md) — Bounded Contexts Entries e Consolidated com tabela de capacidades |
| Identificação de capacidades de negócio | OK | Capacidades listadas com status e prioridade P0/P1/P2 |
| Limites de responsabilidade entre serviços | OK | Database per service; sem acesso cruzado entre BDs; [ADR-001](docs/decisions/ADR-001-microservices.md) |
| Separação lançamentos vs consolidado | OK | Dois serviços independentes; bancos distintos; deploy independente |
| Padrões arquiteturais justificados | OK | 6 ADRs com alternativas consideradas e decisão fundamentada |
| Requisitos funcionais refinados | `OK` | RF1–RF8 com critérios de aceitação em [docs/requirements.md](docs/requirements.md) |
| Requisitos não funcionais definidos | OK | 50 req/s e ≤5% em ADR-005 + k6 com thresholds automáticos |
| Priorização (escopo in/out, riscos) | OK | Tabela P0/P1/P2 em [domain-capabilities.md](docs/architecture/domain-capabilities.md) |

### Diagramas C4

| Critério | Status | Evidência |
|---|:---:|---|
| C4 Context Diagram | OK | [context.md](docs/architecture/context.md) |
| C4 Container Diagram | OK | [container.md](docs/architecture/container.md) |
| C4 Component Diagram | OK | [component.md](docs/architecture/component.md) com diagrama Mermaid por serviço |
| Fluxos de interação entre serviços | OK | Sequence diagrams em component.md; diagrama ASCII no README |

### Segurança

| Critério | Status | Evidência |
|---|:---:|---|
| Autenticação | ~ | JWT real (HMAC-SHA256, exp, iss/aud). Credenciais validadas com PBKDF2. **IdP é demo** — produção requer Keycloak/Entra ID |
| Autorização (policies / isolamento) | OK | Policy `merchant-only` no Gateway (edge) + serviços (defense in depth). `merchantId` sempre do JWT, nunca do body |
| Proteção de APIs (rate limit, validação) | OK | Token Bucket 50 req/s + burst 100 no Gateway. FluentValidation em todos os Commands. JWT enforced no edge (`RequireAuthorization`) |
| Proteção de dados sensíveis | ~ | Secrets em env vars; sem logs de PII; security headers. Sem audit trail nem mascaramento de PII explícito |
| Criptografia (trânsito/repouso) | ~ | TLS termina no Gateway/load balancer (config documentada). Sem criptografia de disco no Docker Compose (requer host-level) |
| Controle de acesso entre serviços | ~ | Portas internas **não expostas** no host. Rede Docker privada. **mTLS s2s não implementado** (ADR documenta como próximo ciclo) |
| Documentação de segurança | OK | [security-design.md](docs/security/security-design.md) com OWASP Top 10, threat model e decisões |
| Segurança implementada no código | OK | PBKDF2, FixedTimeEquals, RBAC, rate limiting, headers, `ExecuteSqlAsync` parametrizado, FluentValidation |

### Implementação e Testes

| Critério | Status | Evidência |
|---|:---:|---|
| Implementação funcional mínima | OK | POST /api/entries, GET /api/entries (paginado, max 100), GET /api/consolidated |
| Testes unitários | OK | 33 testes: Domain, Value Objects, Handlers, Validators |
| Testes de integração | OK | Testcontainers (Postgres real + RabbitMQ real + Redis real) — 8 testes |
| Testes E2E cross-service | OK | [CrossServiceFlowTests.cs](tests/CashFlow.E2E.Tests/CrossServiceFlowTests.cs) — Entries → Outbox → RabbitMQ → Consolidated (polling 15 s) |
| Evidência RNF 50 req/s / ≤5% erro | OK | [baseline-reference.json](tests/load/results/baseline-reference.json) + [load-test.yml](.github/workflows/load-test.yml) gera artefato automático a cada push. Projeção AWS ~80 req/s / Azure ~85 req/s em [rnf-throughput-evidence.md](docs/operations/rnf-throughput-evidence.md) |

### Repositório e Documentação

| Critério | Status | Evidência |
|---|:---:|---|
| Repositório + README | OK | README com Quick Start, matriz, ADRs, estrutura |
| `/docs/architecture` | OK | context, container, component, domain-capabilities |
| `/docs/security` | OK | security-design.md |
| `/docs/decisions` + ADRs | OK | 6 ADRs (001–006) com contexto, decisão e consequências |
| `/docs/operations` | OK | runbook.md com deploy, health, outbox, alertas, CI |

### Operação e RNF

| Critério | Status | Evidência |
|---|:---:|---|
| Deploy / monitoramento / logs | OK | docker-compose, Makefile, Seq (logs), Jaeger (traces), health checks |
| Recuperação de falhas | OK | Runbook §7; retry policy MassTransit; DLQ; Outbox dead-letter cap (10 retries) |
| Idempotência do consumer | OK | Tabela `processed_events(entry_id PK)` + transação atômica — reentregas não duplicam saldo |
| Race condition DailyBalance | OK | PostgreSQL `ON CONFLICT DO UPDATE` — upsert atômico, sem GET+INSERT não atômico |
| Reconciliação de eventos | OK | [scripts/reconcile-outbox.sh](scripts/reconcile-outbox.sh) — diagnóstico + `--restart` + `--purge-old` |
| RNF: Entries independente do Consolidated | OK | Provado arquiteturalmente (async) e pelo E2E (Consolidated pode estar offline; Outbox garante entrega posterior) |
| RNF: 50 req/s consolidado | OK | k6 baseline 52.4 req/s. CI nightly. Projeção AWS/Azure +50% de margem. [rnf-throughput-evidence.md](docs/operations/rnf-throughput-evidence.md) |
| RNF: perda máxima 5% | OK | Baseline 0.21% erro. Threshold automático em CI. Token Bucket + Redis cache-aside garantem estabilidade |
| Diferencial: cloud docs | OK | [cloud.md](docs/architecture/cloud.md) (AWS) e [cloud-azure.md](docs/architecture/cloud-azure.md) |
| Diferencial: observabilidade | OK | OpenTelemetry Traces + **Metrics** (ASP.NET Core + Runtime) + Jaeger + Serilog + Seq + Correlation ID |
| CI (build + testes) | OK | [ci.yml](.github/workflows/ci.yml) — build + unit + integration + E2E em jobs paralelos |

### Síntese dos ajustes aplicados

| Sugestão original | Prioridade | Status |
|---|:---:|:---:|
| `RequireAuthorization` no reverse proxy; não expor portas internas | Alta | Feito |
| Outbox + teste E2E Entries → Consolidated | Alta | Feito |
| Evidenciar 50 req/s com falha ≤5% (k6) | Alta | OK Baseline 52.4 req/s / 0.21% erro; CI nightly gera artefato; projeção AWS/Azure |
| Policies de autorização | Média | Feito (`merchant-only` em edge + serviços) |
| Disclaimer explícito sobre IdP demo | Média | Feito (README + ADR-004 + código) |
| Docs de domínios/capacidades e priorização | Média | Feito ([domain-capabilities.md](docs/architecture/domain-capabilities.md)) |
| CI (build + test) | Baixa | Feito (GitHub Actions 4 jobs) |
| Job de reconciliação | Baixa | Feito ([reconcile-outbox.sh](scripts/reconcile-outbox.sh)) |
