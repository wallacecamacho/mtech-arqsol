# ADR-006: Transactional Outbox para Publicação Confiável de Eventos

**Status:** Aceito
**Data:** 2026-07-17
**Decisores:** Time de Arquitetura
**Depende de:** [ADR-002](ADR-002-async-messaging.md), [ADR-003](ADR-003-cqrs-mediatr.md)

---

## Contexto

Na implementação inicial do `CreateEntryCommandHandler`, a publicação do evento de integração ocorria **após** o `SaveChangesAsync`:

```csharp
await _repository.AddAsync(entry, ct);
await _repository.SaveChangesAsync(ct);          // (1) persiste entry
await _eventBus.PublishAsync(integrationEvent, ct); // (2) publica evento ← DUAL-WRITE
```

Esse padrão cria um **dual-write**: dois sistemas distintos (PostgreSQL e RabbitMQ) precisam ser atualizados atomicamente. Se a aplicação falhar entre `(1)` e `(2)` — crash, timeout de rede, restart do container — o lançamento é persistido mas o evento nunca é publicado. O `Consolidated Service` não saberá do lançamento e o saldo ficará **permanentemente incorreto**.

O problema é inerente: não existe transação distribuída entre PostgreSQL e RabbitMQ sem coordenação externa (2PC/Saga), o que introduz complexidade e pontos de falha adicionais.

## Drivers de Decisão

- **Confiabilidade**: Um lançamento persistido deve **sempre** gerar um evento publicado — sem exceções
- **Simplicidade**: A solução não deve exigir coordenação distribuída (2PC) ou Saga
- **Rastreabilidade**: Mensagens não publicadas devem ser visíveis e reprocessáveis
- **Impacto mínimo**: Não alterar a interface pública dos handlers nem o comportamento do consumidor

## Decisão

Adotar o padrão **Transactional Outbox**: em vez de publicar diretamente no broker, o handler escreve o evento serializado em uma tabela `outbox_messages` **na mesma transação** que persiste o aggregate. Um `BackgroundService` separado faz polling da tabela e publica os eventos pendentes.

### Fluxo

```
Handler (mesma transação DB):
  ┌─────────────────────────────────────────────────┐
  │ 1. entries.AddAsync(entry)                      │
  │ 2. outbox_messages.AddAsync(OutboxMessage)      │
  │    { event_type, payload_json, occurred_at }    │
  │ 3. SaveChangesAsync()  ← atomic commit          │
  └─────────────────────────────────────────────────┘

OutboxProcessorBackgroundService (polling a cada 5s):
  SELECT * FROM outbox_messages WHERE processed_at IS NULL
  FOR EACH message:
    PublishAsync(deserialize(message.payload))
    message.processed_at = NOW()
  SaveChangesAsync()
```

### Garantias

| Cenário | Comportamento |
|---|---|
| Crash entre `AddAsync` e `SaveChanges` | Nenhuma escrita ocorre (transação rollback) |
| Crash após `SaveChanges`, antes do polling | Outbox tem registro pendente; processor publica no próximo ciclo |
| Falha no publish (RabbitMQ offline) | `retry_count++`, `error` registrado; tentativa novamente em 5s |
| Publish bem-sucedido | `processed_at` preenchido; mensagem não será reprocessada |
| Restart do Entries Service | Processor retoma todas as mensagens pendentes automaticamente |

### Estrutura da tabela

```sql
CREATE TABLE outbox_messages (
    id           UUID PRIMARY KEY,
    event_type   VARCHAR(200) NOT NULL,   -- e.g. 'entry.created'
    payload      TEXT NOT NULL,           -- JSON serializado do IntegrationEvent
    occurred_at  TIMESTAMPTZ NOT NULL,
    processed_at TIMESTAMPTZ,             -- NULL = pendente
    error        VARCHAR(2000),           -- último erro de publicação
    retry_count  INT NOT NULL DEFAULT 0
);
CREATE INDEX idx_outbox_processed_at ON outbox_messages(processed_at);
```

### Componentes Adicionados

| Componente | Localização | Responsabilidade |
|---|---|---|
| `OutboxMessage` | `Infrastructure/Persistence/` | Entidade persistida no banco |
| `IOutboxRepository` | `Domain/Repositories/` | Contrato de escrita no Outbox |
| `OutboxRepository` | `Infrastructure/Persistence/` | Implementação EF Core |
| `OutboxProcessorBackgroundService` | `Infrastructure/` | Worker que publica e marca mensagens |
| Migration `20260717000000_AddOutboxMessages` | `Migrations/` | DDL da tabela `outbox_messages` |

## Consequências

**Positivo:**
- [OK] Atomicidade garantida: entry e outbox message são persistidos ou revertidos juntos
- [OK] Nenhum evento é perdido por crash ou falha de rede pós-commit
- [OK] `OutboxProcessorBackgroundService` é resiliente: retry automático, nunca descarta
- [OK] Observabilidade: mensagens com `processed_at IS NULL` por longa data são sintoma de problema no broker
- [OK] Sem alteração no `IEntryRepository` nem na interface HTTP — mudança transparente

**Negativo / Trade-offs:**
- [!] Latência adicional: evento publicado em até ~5s após o commit (em vez de imediato)
- [!] Polling a cada 5s: aceita para o volume atual (50 req/s). Em volumes maiores, considerar change data capture (Debezium) ou advisory locks para reduzir latência
- [!] Requer limpeza periódica da tabela (mensagens processadas acumulam). Recomendado: job de purge com `WHERE processed_at < NOW() - INTERVAL '30 days'`
- [!] At-least-once delivery mantido: consumer do Consolidated deve permanecer idempotente

## Alternativas Rejeitadas

| Alternativa | Motivo da Rejeição |
|---|---|
| Two-Phase Commit (2PC) | Requer coordenador externo; RabbitMQ não suporta XA transactions |
| Saga Coreografada | Complexidade excessiva para um único evento de integração |
| Change Data Capture (Debezium) | Adequado para alto volume; overhead operacional não justificado no MVP |
| Try-catch com reenvio | Não é atômico; falha silenciosa possível se o processo morrer no catch |

## Referências

- [ADR-002: Comunicação Assíncrona via RabbitMQ](ADR-002-async-messaging.md)
- [ADR-003: CQRS com MediatR](ADR-003-cqrs-mediatr.md)
- [Runbook — Monitoramento do Outbox](../operations/runbook.md#10-monitoramento-do-outbox)
