# C4 Model — Component Diagram

> **Nível 3 — Components:** Detalha os blocos internos de cada serviço, suas responsabilidades e como se comunicam. Cada serviço segue **Clean Architecture** com camadas Domain → Application → Infrastructure → API.

---

## Estrutura de Camadas (por serviço)

```
API/              ← Controllers: HTTP in/out, JWT extraction, delegação para MediatR
Application/      ← Commands, Queries, Validators, Handlers, Consumers (CQRS)
Domain/           ← Entities, Aggregates, Value Objects, Domain Events, Interfaces
Infrastructure/   ← EF Core DbContext, Repositories, EventBus (MassTransit), Cache
```

A regra de dependência flui **de fora para dentro**: API → Application → Domain. Infrastructure implementa interfaces definidas no Domain.

---

## Entries Service

> Responsabilidade: **registrar lançamentos financeiros** (débitos e créditos) de um comerciante. Persiste no banco e publica um evento de integração para que outros serviços possam reagir de forma assíncrona.

```mermaid
C4Component
    title CashFlow.Entries — Component Diagram

    Container_Boundary(entries, "CashFlow.Entries") {

        Container_Boundary(api, "API Layer") {
            Component(controller, "EntriesController", "ASP.NET Core Controller", "Recebe requisições HTTP autenticadas. Extrai merchantId do claim 'sub' do JWT. Delega para MediatR. Traduz Result<T> em HTTP response.")
        }

        Container_Boundary(app, "Application Layer") {
            Component(validationBehavior, "ValidationPipelineBehavior", "MediatR Pipeline Behavior", "Intercepta todos os Commands/Queries antes do Handler. Executa FluentValidation. Retorna Result.Failure com erros sem chegar ao Handler.")
            Component(createCmd, "CreateEntryCommand + Handler", "MediatR Command", "Orquestra o caso de uso de criação: valida via pipeline, cria o aggregate Entry, persiste no banco e publica EntryCreatedIntegrationEvent.")
            Component(getQuery, "GetEntriesByDateQuery + Handler", "MediatR Query", "Busca lançamentos por data e merchantId. Retorna lista de EntryDto sem expor o aggregate diretamente.")
        }

        Container_Boundary(domain, "Domain Layer") {
            Component(entry, "Entry", "Aggregate Root", "Encapsula regras de negócio: amount > 0, description não vazia, data não futura. Emite EntryCreatedDomainEvent ao ser criado.")
            Component(money, "Money", "Value Object", "Imutável. Encapsula Amount (decimal) e Currency (ISO 4217). Garante amount >= 0. Operações Add/Subtract com validação de moeda igual.")
        }

        Container_Boundary(infra, "Infrastructure Layer") {
            Component(entryRepo, "EntryRepository", "EF Core Repository", "Implementa IEntryRepository definida no Domain. Métodos: GetByIdAsync, GetByDateAsync, AddAsync, SaveChangesAsync.")
            Component(dbCtx, "EntriesDbContext", "EF Core DbContext", "Mapeia Entry para tabela 'entries'. OwnsOne para Money. Índice composto em (merchant_id, entry_date).")
            Component(eventbus, "MassTransitEventBus", "Event Bus", "Implementa IEventBus. Usa IBus do MassTransit para publicar IntegrationEvents no RabbitMQ.")
        }
    }

    Rel(controller, validationBehavior, "Send(Command/Query)", "MediatR pipeline")
    Rel(validationBehavior, createCmd, "next() se válido")
    Rel(validationBehavior, getQuery, "next() se válido")
    Rel(createCmd, entry, "Entry.Create(amount, currency, type, description, date, merchantId)")
    Rel(createCmd, entryRepo, "AddAsync(entry) + SaveChangesAsync()")
    Rel(createCmd, eventbus, "PublishAsync(EntryCreatedIntegrationEvent)")
    Rel(getQuery, entryRepo, "GetByDateAsync(merchantId, date)")
    Rel(entry, money, "Owns")
    Rel(entryRepo, dbCtx, "LINQ queries via EF Core")
```

### Responsabilidades por Componente — Entries Service

| Componente | Camada | Responsabilidade principal | Dependências |
|---|---|---|---|
| `EntriesController` | API | HTTP I/O, extração de `merchantId` do JWT, mapeamento Result → HTTP | MediatR |
| `ValidationPipelineBehavior` | Application | Validação transversal antes de qualquer Handler | FluentValidation |
| `CreateEntryCommand + Handler` | Application | Caso de uso: criar lançamento, persistir, publicar evento | `IEntryRepository`, `IEventBus` |
| `GetEntriesByDateQuery + Handler` | Application | Caso de uso: listar lançamentos por data | `IEntryRepository` |
| `Entry` (Aggregate) | Domain | Regras de negócio do lançamento; gera `EntryCreatedDomainEvent` | `Money` |
| `Money` (Value Object) | Domain | Imutabilidade e validação monetária | — |
| `IEntryRepository` | Domain | Contrato de persistência (interface) | — |
| `EntryRepository` | Infrastructure | Implementação EF Core do contrato | `EntriesDbContext` |
| `MassTransitEventBus` | Infrastructure | Publicação de eventos no RabbitMQ | MassTransit `IBus` |
| `EntriesDbContext` | Infrastructure | Mapeamento ORM, configuração de índices | EF Core + Npgsql |

---

## Consolidated Service

> Responsabilidade: **consolidar o saldo diário por comerciante**. Consome eventos publicados pelo Entries Service de forma assíncrona, acumula créditos e débitos no aggregate `DailyBalance`, e serve consultas de saldo com cache Redis.

```mermaid
C4Component
    title CashFlow.Consolidated — Component Diagram

    Container_Boundary(consolidated, "CashFlow.Consolidated") {

        Container_Boundary(api2, "API Layer") {
            Component(controller, "ConsolidatedController", "ASP.NET Core Controller", "Expõe GET /api/consolidated/{date} e GET /api/consolidated (hoje). Extrai merchantId do JWT. Retorna DailyBalanceDto ou 404 se não houver saldo.")
        }

        Container_Boundary(app2, "Application Layer") {
            Component(balanceQuery, "GetDailyBalanceQuery + Handler", "MediatR Query", "Cache-aside: verifica Redis primeiro. Se miss, busca no banco, popula cache com TTL adaptativo (5min hoje / 24h histórico) e retorna DTO.")
            Component(consumer, "EntryCreatedConsumer", "MassTransit Consumer", "Consome EntryCreatedIntegrationEvent do RabbitMQ. Cria ou atualiza DailyBalance. Invalida cache Redis após persistir.")
        }

        Container_Boundary(domain2, "Domain Layer") {
            Component(dailyBalance, "DailyBalance", "Aggregate Root", "Acumula TotalCredits e TotalDebits. Balance = Credits - Debits. Valida que valores aplicados sejam positivos.")
        }

        Container_Boundary(infra2, "Infrastructure Layer") {
            Component(balanceRepo, "DailyBalanceRepository", "EF Core Repository", "Implementa IDailyBalanceRepository. Métodos: GetByMerchantAndDateAsync, GetByMerchantAndDateRangeAsync, AddAsync, Update, SaveChangesAsync.")
            Component(cache, "Redis Cache", "IDistributedCache", "StackExchange.Redis. Chave: 'dailybalance:{merchantId}:{yyyy-MM-dd}'. TTL 5min (hoje) / 24h (histórico). Lido pela Query e invalidado pelo Consumer.")
            Component(dbCtx, "ConsolidatedDbContext", "EF Core DbContext", "Mapeia DailyBalance para 'daily_balances'. Índice único em (merchant_id, date).")
        }
    }

    Rel(controller, balanceQuery, "Send(GetDailyBalanceQuery)", "MediatR in-process")
    Rel(balanceQuery, cache, "GetStringAsync / SetStringAsync")
    Rel(balanceQuery, balanceRepo, "GetByMerchantAndDateAsync(merchantId, date)")
    Rel(consumer, dailyBalance, "ApplyCredit(amount) / ApplyDebit(amount)")
    Rel(consumer, balanceRepo, "GetByMerchantAndDateAsync + Update + SaveChangesAsync")
    Rel(consumer, cache, "RemoveAsync(cacheKey)")
    Rel(balanceRepo, dbCtx, "LINQ queries via EF Core")
```

### Responsabilidades por Componente — Consolidated Service

| Componente | Camada | Responsabilidade principal | Dependências |
|---|---|---|---|
| `ConsolidatedController` | API | HTTP I/O, extração de `merchantId` do JWT, roteamento por data | MediatR |
| `GetDailyBalanceQuery + Handler` | Application | Cache-aside: Redis → DB → Redis (fill) | `IDailyBalanceRepository`, `IDistributedCache` |
| `EntryCreatedConsumer` | Application | Projeção de eventos: atualiza saldo + invalida cache | `IDailyBalanceRepository`, `IDistributedCache` |
| `DailyBalance` (Aggregate) | Domain | Acumulação de créditos/débitos; regra Balance = Credits - Debits | — |
| `IDailyBalanceRepository` | Domain | Contrato de persistência (interface) | — |
| `DailyBalanceRepository` | Infrastructure | Implementação EF Core do contrato | `ConsolidatedDbContext` |
| Redis (`IDistributedCache`) | Infrastructure | Cache de leitura (TTL 5 min/24h) e invalidação pós-evento | StackExchange.Redis |
| `ConsolidatedDbContext` | Infrastructure | Mapeamento ORM, índice único merchant+date | EF Core + Npgsql |

---

## Fluxo: Criação de Lançamento e Consolidação

> Demonstra o caminho completo de uma requisição, incluindo o desacoplamento assíncrono entre os dois serviços e o comportamento do cache Redis.

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
    ES->>ES: ValidationPipelineBehavior (FluentValidation)
    ES->>ES: Entry.Create() — domain rules
    ES->>DB1: INSERT entry
    ES->>MQ: Publish EntryCreatedIntegrationEvent
    ES->>GW: 201 Created {id}
    GW->>C: 201 Created {id}

    Note over MQ,CS: Assíncrono — Consolidated pode estar indisponível sem impactar Entries

    MQ-->>CS: Deliver EntryCreatedIntegrationEvent (retry: 500ms/1s/2s/5s)
    CS->>DB2: SELECT daily_balance WHERE merchant_id AND date
    alt Balance não existe
        CS->>DB2: INSERT daily_balance
    else Balance existe
        CS->>DB2: UPDATE (ApplyCredit / ApplyDebit)
    end
    CS->>RD: DEL cache key (invalida saldo desatualizado)

    C->>GW: GET /api/consolidated/2024-01-15 (JWT)
    GW->>CS: Proxy GET
    CS->>RD: GET cache key
    alt Cache hit (TTL ativo)
        CS->>GW: 200 OK {balance} — resposta < 1ms
    else Cache miss
        CS->>DB2: SELECT daily_balance WHERE merchant_id AND date
        CS->>RD: SET cache (TTL 5min se hoje / 24h se histórico)
        CS->>GW: 200 OK {balance}
    end
    GW->>C: 200 OK {balance}
```

---

## Navegação

| Nível | Arquivo |
|---|---|
| Context Diagram (visão de negócio) | [context.md](context.md) |
| Container Diagram (serviços e infra) | [container.md](container.md) |
| Cloud Architecture (AWS) | [cloud.md](cloud.md) |
| Cloud Architecture (Azure) | [cloud-azure.md](cloud-azure.md) |
