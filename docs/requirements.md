# Requisitos — CashFlow

> Documento de requisitos do desafio técnico para Arquiteto de Soluções.  
> Referência: [ADR-001](decisions/ADR-001-microservices.md) · [domain-capabilities.md](architecture/domain-capabilities.md)

---

## Requisitos Funcionais

| ID | Requisito | Critério de Aceitação | Prioridade | Status |
|---|---|---|:---:|:---:|
| RF1 | Registrar lançamento financeiro (crédito ou débito) | `POST /api/entries` retorna `201` com o ID do lançamento; lançamento persiste no banco `cashflow_entries` | P0 | Implementado |
| RF2 | Consultar lançamentos de um comerciante por data | `GET /api/entries?date=YYYY-MM-DD` retorna lista paginada (`page`, `pageSize`) com headers `X-Total-Count`; resultados isolados por `merchantId` do JWT | P0 | Implementado |
| RF3 | Consultar saldo consolidado diário | `GET /api/consolidated/{date}` retorna `TotalCredits`, `TotalDebits` e `Balance` do dia; `404` quando não há lançamentos | P0 | Implementado |
| RF4 | Consistência eventual via eventos | Lançamento criado deve aparecer no saldo consolidado em até ~10 segundos (tempo de polling do Outbox + propagação RabbitMQ) | P0 | Implementado |
| RF5 | Consultar histórico de saldos por período | `GET /api/consolidated/history?from=YYYY-MM-DD&to=YYYY-MM-DD` retorna série temporal de saldos; máximo 365 dias | P1 | Implementado |
| RF6 | Autenticação de comerciantes | `POST /api/auth/token` com `{username, password}` retorna JWT com claims `sub` (merchantId), `role` e `exp` | P0 | Implementado (demo) |
| RF7 | Isolamento de dados por comerciante | Cada comerciante acessa apenas seus próprios lançamentos e saldos; `merchantId` extraído do JWT, nunca do body | P0 | Implementado |
| RF8 | Resiliência: Entries independente do Consolidated | `CashFlow.Entries` continua funcionando quando `CashFlow.Consolidated` está offline | P0 | Implementado |

---

## Requisitos Não Funcionais

| ID | Requisito | Threshold | Como medir | Status |
|---|---|---|---|:---:|
| RNF1 | Throughput sustentado | 50 req/s no `GET /api/consolidated` | k6 load test: `make load-test` (threshold `rate >= 50`) | Atendido — baseline 52,4 req/s |
| RNF2 | Taxa de erro sob carga | <= 5% | k6 threshold: `http_req_failed < 0.05` | Atendido — baseline 0,21% |
| RNF3 | Latência p95 | < 500 ms | k6 threshold: `p(95) < 500` | Atendido — baseline p95=187ms |
| RNF4 | Confiabilidade de eventos | 0 eventos perdidos | Outbox: `SELECT COUNT(*) FROM outbox_messages WHERE processed_at IS NULL` deve chegar a 0 | Atendido — Outbox atômico |
| RNF5 | Idempotência do consumer | Reentrega do mesmo evento não duplica saldo | Tabela `processed_events(entry_id PK)` | Atendido |

---

## Escopo

### In Scope (v1.0 — este repositório)
- Registro de lançamentos (débito/crédito) com validação de domínio
- Consulta de lançamentos por data (paginada)
- Consolidação assíncrona via Transactional Outbox + RabbitMQ
- Consulta de saldo diário e histórico de saldos por período
- Autenticação JWT (demo com PBKDF2)
- Autorização RBAC (`merchant-only` policy)
- Rate limiting (Token Bucket 50 req/s no Gateway)
- Testes: unit, integration (Testcontainers), E2E cross-service, load test k6
- CI: GitHub Actions (build + unit + integration + E2E + load-test nightly)
- Observabilidade: Serilog + Seq + OpenTelemetry (Traces + Metrics) + Jaeger

### Out of Scope (próximos ciclos)
| Item | Justificativa | Prioridade futura |
|---|---|:---:|
| Cancelamento / estorno de lançamentos | Sem requisito explícito no enunciado | P2 |
| IdP real (Keycloak / Azure Entra ID) | Complexidade não justificada no MVP | P2 |
| mTLS entre serviços (s2s) | Requer cert-manager / Istio | P2 |
| Rate limiting por `merchantId` | Multi-tenant fairness — Redis sliding window | P2 |
| Exportação CSV/PDF | Não mencionado no enunciado | P3 |
| Multi-moeda com conversão | Sistema atual usa BRL por padrão | P3 |
| GDPR / mascaramento de PII nos logs | Não avaliado | P3 |

---

## Referências

- [ADR-001: Arquitetura de Microservices](decisions/ADR-001-microservices.md)
- [ADR-002: Comunicação Assíncrona via RabbitMQ](decisions/ADR-002-async-messaging.md)
- [ADR-006: Transactional Outbox](decisions/ADR-006-outbox-pattern.md)
- [Domain Capabilities Map](architecture/domain-capabilities.md)
- [Evidência de RNF — Throughput](../docs/operations/rnf-throughput-evidence.md)
