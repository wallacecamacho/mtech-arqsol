# C4 Model — Container Diagram

> Nível 2: Containers que compõem o sistema CashFlow e suas responsabilidades.

```mermaid
C4Container
    title CashFlow System — Container Diagram

    Person(merchant, "Comerciante", "Usuário final")

    System_Boundary(cashflow, "CashFlow System") {
        Container(gateway, "API Gateway", ".NET 8 / YARP", "Ponto de entrada único. JWT validation, Rate Limiting (50 req/s token bucket), CORS, Routing.")
        Container(entries, "Entries Service", ".NET 8 / ASP.NET Core", "Gerencia lançamentos (débitos/créditos). Clean Architecture. CQRS via MediatR.")
        Container(consolidated, "Consolidated Service", ".NET 8 / ASP.NET Core", "Consolida saldo diário. Consome eventos do RabbitMQ. Cache Redis.")
        ContainerDb(entriesDb, "Entries DB", "PostgreSQL 16", "Persistência dos lançamentos.")
        ContainerDb(consolidatedDb, "Consolidated DB", "PostgreSQL 16", "Persistência dos saldos diários.")
        ContainerQueue(mq, "Message Broker", "RabbitMQ 3", "Desacopla Entries do Consolidated. Fila: cashflow.consolidated.entry-created")
        ContainerDb(cache, "Cache", "Redis 7", "Cache dos saldos diários (TTL 5 min / 24h para histórico).")
    }

    System_Ext(seq, "Seq", "Log aggregation")
    System_Ext(jaeger, "Jaeger", "Distributed tracing")

    Rel(merchant, gateway, "REST API calls", "HTTPS :8000")
    Rel(gateway, entries, "Proxy /api/entries/*", "HTTP (internal)")
    Rel(gateway, consolidated, "Proxy /api/consolidated/*", "HTTP (internal)")
    Rel(entries, entriesDb, "Read/Write", "TCP 5432")
    Rel(entries, mq, "Publish EntryCreatedIntegrationEvent", "AMQP")
    Rel(consolidated, mq, "Consume EntryCreatedIntegrationEvent", "AMQP")
    Rel(consolidated, consolidatedDb, "Read/Write", "TCP 5432")
    Rel(consolidated, cache, "Get/Set DailyBalance", "TCP 6379")
    Rel(entries, seq, "Structured logs", "HTTP 5341")
    Rel(consolidated, seq, "Structured logs", "HTTP 5341")
    Rel(gateway, seq, "Structured logs", "HTTP 5341")
    Rel(entries, jaeger, "Traces", "gRPC 4317 (OTLP)")
    Rel(consolidated, jaeger, "Traces", "gRPC 4317 (OTLP)")
    Rel(gateway, jaeger, "Traces", "gRPC 4317 (OTLP)")
```

## Decisões de Isolamento

| Decisão | Justificativa |
|---|---|
| Banco de dados separado por serviço | Evita acoplamento de schema. Cada serviço evolui independentemente. |
| Comunicação assíncrona via RabbitMQ | Entries continua operando se Consolidated cair (requisito não-funcional explícito) |
| Cache Redis no Consolidated | Atende picos de 50 req/s sem bater no banco a cada requisição |
| API Gateway como único ponto de entrada | Centraliza autenticação, rate limiting e observabilidade |
