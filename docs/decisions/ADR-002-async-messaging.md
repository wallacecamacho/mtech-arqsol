# ADR-002: Comunicação Assíncrona via RabbitMQ

**Status:** Aceito
**Data:** 2024-01-01
**Decisores:** Time de Arquitetura
**Depende de:** [ADR-001](ADR-001-microservices.md)

---

## Contexto

Com a arquitetura de microservices definida (ADR-001), o `Consolidated Service` precisa ser notificado sempre que um lançamento é criado pelo `Entries Service` para atualizar o saldo diário. Existem duas formas de implementar essa notificação: síncrona (chamada HTTP direta) ou assíncrona (mensageria).

O requisito não-funcional central é: *"O serviço de controle de lançamentos não deve ficar indisponível caso o serviço de consolidado diário falhe."* Uma chamada HTTP síncrona violaria esse requisito: se o Consolidated estiver offline, o Entries falharia ao tentar notificá-lo.

## Drivers de Decisão

- **RNF1**: Entries não pode propagar falha do Consolidated
- **RNF2**: Eventos não podem ser perdidos se o Consolidated estiver temporiamente fora
- **RNF3**: Retry automático em caso de falha transiente no processamento
- **RNF4**: Rastreabilidade do fluxo de eventos (Correlation ID + tracing)

## Alternativas Consideradas

| Aspecto | HTTP Síncrono | **RabbitMQ + MassTransit** | Apache Kafka |
|---|---|---|---|
| Disponibilidade do Entries | [x] Degradada se Consolidated cair | [OK] Totalmente independente | [OK] Totalmente independente |
| Consistência do consolidado | [OK] Imediata | [!] Eventual (~segundos) | [!] Eventual |
| Retry automático | [x] Manual / circuit breaker | [OK] Nativo via MassTransit | [OK] Nativo |
| Dead-letter queue | [x] Não nativo | [OK] Nativo | [OK] Nativo |
| Complexidade operacional | [OK] Mínima | [!] Média | [x] Alta |
| Self-hosted portável | — | [OK] Sim | [!] Requer Zookeeper/KRaft |
| Volume atual | — | [OK] Adequado | [!] Overkill |

## Decisão

Usar **RabbitMQ 3** como message broker com **MassTransit 8** como camada de abstração. O Entries publica um `EntryCreatedIntegrationEvent`; o Consolidated consome de forma assíncrona e independente.

MassTransit abstrai o broker do código de aplicação: a interface `IEventBus` é definida no SharedKernel, e a implementação com MassTransit fica na camada de Infrastructure. Isso permite trocar o broker (ex: Azure Service Bus) sem alterar a lógica de negócio.

## Configuração de Produção

| Parâmetro | Valor | Justificativa |
|---|---|---|
| Fila | `cashflow.consolidated.entry-created` | Nome descritivo por serviço consumidor |
| Retry policy | 500ms → 1s → 2s → 5s (4 tentativas) | Back-off incremental para falhas transientes |
| PrefetchCount | 10 | Controla throughput sem sobrecarregar o consumer |
| Acknowledgment | Após processamento bem-sucedido | Garante at-least-once delivery |
| Dead-letter queue | `cashflow.consolidated.entry-created_error` | Mensagens após 4 falhas vão para DLQ |
| Credenciais | Usuário `cashflow` (não `guest`) | Sem privilégios de admin |

## Consequências

**Positivo:**
- [OK] Entries opera normalmente mesmo com Consolidated offline
- [OK] Mensagens são persistidas no broker durante indisponibilidade do consumer
- [OK] Retry automático trata falhas transientes sem intervenção manual
- [OK] DLQ garante que mensagens nunca são perdidas silenciosamente
- [OK] Abstração via `IEventBus` facilita troca de broker em produção (Azure Service Bus)

**Negativo / Trade-offs:**
- [!] Consistência eventual: saldo consolidado pode ter atraso de segundos
- [!] Requer monitoramento da DLQ (alerta recomendado: `DLQNotEmpty`)
- [!] At-least-once delivery: o consumer deve ser **idempotente** (implementado via upsert no aggregate)

## Nota sobre Idempotência

O consumer `EntryCreatedConsumer` faz `GetByMerchantAndDateAsync` antes de inserir: se o `DailyBalance` já existir, apenas aplica o crédito/débito incremental. Isso torna o processamento seguro em caso de reentrega da mensagem.

## Referências

- [Component Diagram — Consolidated Service](../architecture/component.md)
- [Runbook — Recuperação de Falhas](../operations/runbook.md)
- [ADR-001: Arquitetura de Microservices](ADR-001-microservices.md)
