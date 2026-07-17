# Domain Capabilities Map

## Bounded Contexts

| Bounded Context | Responsabilidade | Serviço |
|---|---|---|
| **Lançamentos** (Entries) | Registrar e consultar lançamentos financeiros (débito/crédito) por comerciante | `CashFlow.Entries` |
| **Consolidação** (Consolidated) | Calcular e expor saldo diário consolidado por comerciante | `CashFlow.Consolidated` |

---

## Capacidades por Contexto

### Contexto: Lançamentos

| Capacidade | Descrição | Status | Prioridade |
|---|---|---|---|
| Registrar lançamento | `POST /api/entries` — cria lançamento com validação de domínio | ✅ Implementado | P0 |
| Consultar lançamentos por data | `GET /api/entries?date=` — lista lançamentos do comerciante | ✅ Implementado | P0 |
| Publicar evento de integração | Emite `EntryCreatedIntegrationEvent` via RabbitMQ (Outbox) | ✅ Implementado | P0 |
| Garantia de entrega (Outbox) | Salvar evento no banco antes de publicar; retry via background service | ✅ Implementado | P0 |
| Consulta por período | `GET /api/entries?from=&to=` — range de datas | ⚠️ Parcial (repository tem método, sem endpoint) | P1 |
| Cancelamento de lançamento | Marcar lançamento como cancelado (sem deleção física) | ❌ Não implementado | P2 |
| Paginação de resultados | Cursor/offset pagination para grandes volumes | ❌ Não implementado | P2 |

### Contexto: Consolidação

| Capacidade | Descrição | Status | Prioridade |
|---|---|---|---|
| Calcular saldo diário | Consumir `EntryCreatedIntegrationEvent` e agregar por dia/comerciante | ✅ Implementado | P0 |
| Consultar saldo por data | `GET /api/consolidated/{date}` | ✅ Implementado | P0 |
| Consultar saldo atual | `GET /api/consolidated` (data = hoje) | ✅ Implementado | P0 |
| Cache de saldo (Redis) | Evitar recalcular em cada requisição | ✅ Implementado | P1 |
| Reprocessamento de eventos perdidos | Re-trigger de consolidação para uma data | ❌ Não implementado | P1 |
| Histórico de saldos | `GET /api/consolidated?from=&to=` — série temporal | ❌ Não implementado | P2 |
| Exportação CSV/PDF | Download do extrato consolidado | ❌ Não implementado | P3 |

---

## Capacidades Transversais (Cross-cutting)

| Capacidade | Status | Prioridade |
|---|---|---|
| Autenticação JWT (Gateway) | ✅ Implementado — validação no edge | P0 |
| Autorização RBAC (`merchant-only` policy) | ✅ Implementado | P0 |
| Rate limiting (50 req/s, Token Bucket) | ✅ Implementado no Gateway | P0 |
| Observabilidade (traces, logs, métricas) | ✅ OpenTelemetry + Jaeger + Seq | P1 |
| CI automático (build + testes) | ✅ GitHub Actions | P1 |
| Health checks (live/ready) | ✅ Todos os serviços | P1 |
| Load test automatizado | ✅ k6 (`tests/load/k6-load.js`) | P1 |
| Segurança de rede (portas internas isoladas) | ✅ Backend sem ports expostos no Docker | P0 |
| mTLS entre serviços (s2s) | ❌ Não implementado — apenas no design | P2 |
| E2E tests automatizados | ❌ Placeholder no CI | P2 |
| IdP real (Keycloak / Entra ID) | ❌ AuthController é demo | P2 |
| GDPR / mascaramento de dados PII | ❌ Não avaliado | P3 |

---

## Priorização de Backlog

| Prioridade | Critério |
|---|---|
| **P0** | Bloqueia go-live; impacto de segurança ou perda de dados |
| **P1** | Necessário para produção, mas tem workaround aceitável |
| **P2** | Melhoria importante para escala ou UX |
| **P3** | Desejável; adiar para ciclos futuros |
