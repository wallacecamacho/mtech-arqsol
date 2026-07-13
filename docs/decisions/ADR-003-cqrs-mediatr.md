# ADR-003: CQRS com MediatR e Clean Architecture

**Status:** Aceito
**Data:** 2024-01-01
**Decisores:** Time de Arquitetura
**Depende de:** [ADR-001](ADR-001-microservices.md)

---

## Contexto

Cada microserviço precisa de uma estrutura interna clara que separe a lógica de negócio do mecanismo de entrega HTTP, facilite testes unitários e permita adicionar cross-cutting concerns (validação, logging, métricas) sem poluir os casos de uso.

Os dois serviços possuem operações bem distintas:
- **Entries**: escrever lançamentos (Command) e ler lançamentos por data (Query)
- **Consolidated**: ler saldo diário (Query) e processar eventos (Consumer)

Essa assimetria de leitura vs. escrita torna CQRS uma escolha natural.

## Drivers de Decisão

- **Testabilidade**: Handlers devem ser testáveis sem dependência de HTTP ou banco real
- **Separação de responsabilidades**: Controladores não devem conter lógica de negócio
- **Validação declarativa**: Regras de validação separadas do fluxo principal
- **Extensibilidade**: Fácil adição de logging, auditoria e métricas sem alterar handlers

## Decisão

Adotar **CQRS** (Command Query Responsibility Segregation) implementado com **MediatR 12** dentro de cada serviço, seguindo **Clean Architecture** com 4 camadas:

```
API/           ← Controllers: HTTP in/out, extração de claims, delegação para MediatR
Application/   ← Commands, Queries, Handlers, Validators, Consumers (casos de uso)
Domain/        ← Entities, Aggregates, Value Objects, Domain Events, Interfaces
Infrastructure/← EF Core, Repositories, EventBus, Cache (implementa contratos do Domain)
```

A **regra de dependência**: API → Application → Domain. Infrastructure implementa interfaces definidas no Domain (Dependency Inversion).

### Estrutura de Commands e Queries

```
Application/
  Commands/
    CreateEntryCommand.cs          → record : IRequest<Result<Guid>>
    CreateEntryCommandHandler.cs   → IRequestHandler<CreateEntryCommand, Result<Guid>>
    CreateEntryCommandValidator.cs → AbstractValidator<CreateEntryCommand> (FluentValidation)
  Queries/
    GetEntriesByDateQuery.cs       → record : IRequest<Result<IEnumerable<EntryDto>>>
    GetEntriesByDateQueryHandler.cs
  Behaviors/
    ValidationPipelineBehavior.cs  → IPipelineBehavior<TRequest, TResponse>
```

### Pipeline de Processamento

```
HTTP Request
  ↓
Controller (extrai merchantId do JWT, monta Command/Query)
  ↓
MediatR Send()
  ↓
ValidationPipelineBehavior (FluentValidation — retorna Result.Failure se inválido)
  ↓
Handler (lógica de negócio: Domain + Repository + EventBus)
  ↓
Result<T> → Controller traduz para HTTP response
```

## Consequências

**Positivo:**
- ✅ Handlers são unitários puro: testados com mocks de `IEntryRepository` e `IEventBus` sem HTTP
- ✅ `ValidationPipelineBehavior` é transversal: toda validação acontece antes do handler sem código duplicado
- ✅ Adicionar logging ou auditoria = adicionar um novo `IPipelineBehavior`, sem tocar handlers
- ✅ `Result<T>` elimina exceções para controle de fluxo (erros de validação retornam 400, não 500)
- ✅ Controllers são finos: nenhuma lógica de negócio, apenas tradução HTTP

**Negativo / Trade-offs:**
- ⚠️ Mais arquivos para operações simples (Command + Handler + Validator = 3 classes)
- ⚠️ MediatR é um mediator global in-process — não adequado para comunicação entre serviços (para isso usamos RabbitMQ, ADR-002)
- ⚠️ CQRS sem bancos separados (read/write): a otimização de read replica pode ser adicionada futuramente sem mudar a arquitetura

## Alternativas Rejeitadas

| Alternativa | Motivo da Rejeição |
|---|---|
| Service layer tradicional | Mistura leitura e escrita. Tende a crescer em responsabilidades ("God Service"). Validação acoplada ao fluxo. |
| Controllers fat | Viola Single Responsibility. Dificulta testes. |
| CQRS com bancos separados (read/write) | Overkill para o volume atual. Adiciona consistência eventual interna sem benefício justificado. |

## Referências

- [Component Diagram — Entries Service](../architecture/component.md)
- [ADR-002: Comunicação Assíncrona](ADR-002-async-messaging.md)
