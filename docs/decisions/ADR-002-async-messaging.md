# ADR-002: Comunicação Assíncrona via RabbitMQ

**Status:** Aceito  
**Data:** 2024-01-01

## Contexto

O Consolidated Service precisa ser notificado quando um lançamento é criado, para atualizar o saldo diário. O requisito não-funcional exige que o Entries Service **continue operando** mesmo que o Consolidated esteja indisponível.

## Decisão

Usar comunicação **assíncrona via mensageria (RabbitMQ)** com MassTransit como abstração. O Entries Service publica um `EntryCreatedIntegrationEvent` no broker. O Consolidated Service consome esse evento de forma independente.

## Trade-offs

| Aspecto | Síncrono (HTTP) | Assíncrono (RabbitMQ) — **Escolhido** |
|---|---|---|
| Disponibilidade de Entries | Degradada se Consolidated cair | **Totalmente independente** |
| Consistência do consolidado | Imediata | Eventual (segundos de latência) |
| Complexidade | Baixa | Média |
| Retry em falha | Manual / circuit breaker | **Automático via MassTransit** |
| Rastreabilidade | Simples | Requer correlation ID |

## Justificativa

O requisito não-funcional é explícito: *"O serviço de controle de lançamentos não deve ficar indisponível caso o serviço de consolidado diário falhe."* A comunicação síncrona (HTTP direto) cria um acoplamento temporal que violaria esse requisito. A mensageria garante que o evento é persistido no broker mesmo se o consumidor estiver offline.

## Configuração

- Exchange/Queue: `cashflow.consolidated.entry-created`
- Retry policy: intervalos 500ms, 1s, 2s, 5s (4 tentativas)
- PrefetchCount: 10 (controle de throughput)
- Acknowledgment: automático após processamento bem-sucedido

## Alternativas Rejeitadas

- **Azure Service Bus**: Exige infraestrutura cloud. RabbitMQ é self-hosted e portável.
- **Kafka**: Overkill para o volume atual. Complexidade operacional não justificada.
- **HTTP síncrono**: Viola requisito de disponibilidade.
