# C4 Model — Component Diagram

> Nível 3: Componentes internos do Entries Service e Consolidated Service.

## Entries Service

```mermaid
C4Component
    title CashFlow.Entries — Component Diagram

    Container_Boundary(entries, "CashFlow.Entries") {
        Component(controller, "EntriesController", "ASP.NET Core Controller", "Recebe requisições HTTP. Valida JWT. Encaminha para MediatR.")
        Component(createCmd, "CreateEntryCommand + Handler", "MediatR Command", "Valida dados (FluentValidation). Cria Entry aggregate. Persiste. Publica IntegrationEvent.")
        Component(getQuery, "GetEntriesByDateQuery + Handler", "MediatR Query", "Busca lançamentos por data e merchantId.")
        Component(entry, "Entry Aggregate", "Domain Entity", "Encapsula regras de negócio. Gera EntryCreatedDomainEvent.")
        Component(money, "Money ValueObject", "Domain Value Object", "Encapsula valor monetário e moeda.")
        Component(repo, "EntryRepository", "Infrastructure", "Acesso ao banco via EF Core.")
        Component(eventbus, "MassTransitEventBus", "Infrastructure", "Publica IntegrationEvents no RabbitMQ via MassTransit.")
        Component(dbCtx, "EntriesDbContext", "EF Core DbContext", "Mapeamento ORM para PostgreSQL.")
    }

    Rel(controller, createCmd, "Send(CreateEntryCommand)")
    Rel(controller, getQuery, "Send(GetEntriesByDateQuery)")
    Rel(createCmd, entry, "Entry.Create()")
    Rel(createCmd, repo, "AddAsync + SaveChangesAsync")
    Rel(createCmd, eventbus, "PublishAsync(EntryCreatedIntegrationEvent)")
    Rel(getQuery, repo, "GetByDateAsync")
    Rel(repo, dbCtx, "EF Core operations")
    Rel(entry, money, "Owns")
```

## Consolidated Service

```mermaid
C4Component
    title CashFlow.Consolidated — Component Diagram

    Container_Boundary(consolidated, "CashFlow.Consolidated") {
        Component(controller, "ConsolidatedController", "ASP.NET Core Controller", "Expõe GET /api/consolidated/{date}.")
        Component(balanceQuery, "GetDailyBalanceQuery + Handler", "MediatR Query", "Busca saldo. Verifica cache Redis primeiro.")
        Component(consumer, "EntryCreatedConsumer", "MassTransit Consumer", "Consome eventos do RabbitMQ. Aplica crédito/débito no DailyBalance. Retry automático.")
        Component(dailyBalance, "DailyBalance Aggregate", "Domain Entity", "Acumula TotalCredits e TotalDebits. Calcula Balance.")
        Component(repo, "DailyBalanceRepository", "Infrastructure", "Acesso ao banco via EF Core.")
        Component(cache, "Redis Distributed Cache", "Infrastructure", "IDistributedCache com StackExchange.Redis.")
        Component(dbCtx, "ConsolidatedDbContext", "EF Core DbContext", "Mapeamento ORM para PostgreSQL.")
    }

    Rel(controller, balanceQuery, "Send(GetDailyBalanceQuery)")
    Rel(balanceQuery, cache, "GetStringAsync / SetStringAsync")
    Rel(balanceQuery, repo, "GetByMerchantAndDateAsync")
    Rel(consumer, repo, "GetByMerchantAndDateAsync + Update + SaveChanges")
    Rel(consumer, dailyBalance, "ApplyCredit / ApplyDebit")
    Rel(consumer, cache, "RemoveAsync (invalidate cache key)")
    Rel(repo, dbCtx, "EF Core operations")
```

## Fluxo: Criação de Lançamento e Consolidação

```mermaid
sequenceDiagram
    participant C as Comerciante
    participant GW as API Gateway
    participant ES as Entries Service
    participant MQ as RabbitMQ
    participant CS as Consolidated Service
    participant DB1 as PostgreSQL (entries)
    participant DB2 as PostgreSQL (consolidated)
    participant RD as Redis

    C->>GW: POST /api/entries (JWT)
    GW->>GW: Validate JWT + Rate Limit check
    GW->>ES: Proxy POST /api/entries
    ES->>ES: Validate command (FluentValidation)
    ES->>DB1: INSERT entry
    ES->>MQ: Publish EntryCreatedIntegrationEvent
    ES->>GW: 201 Created {id}
    GW->>C: 201 Created {id}

    Note over MQ,CS: Assíncrono — Consolidated pode estar indisponível sem impactar Entries

    MQ-->>CS: Deliver EntryCreatedIntegrationEvent
    CS->>DB2: SELECT daily_balance WHERE merchant_id AND date
    alt Balance não existe
        CS->>DB2: INSERT daily_balance
    else Balance existe
        CS->>DB2: UPDATE (ApplyCredit/ApplyDebit)
    end
    CS->>RD: DEL cache key (invalidate)

    C->>GW: GET /api/consolidated/2024-01-15 (JWT)
    GW->>CS: Proxy GET
    CS->>RD: GET cache key
    alt Cache hit
        CS->>GW: 200 OK (from cache)
    else Cache miss
        CS->>DB2: SELECT daily_balance
        CS->>RD: SET cache (TTL 5min)
        CS->>GW: 200 OK
    end
    GW->>C: 200 OK {balance}
```
