# ADR-003: CQRS com MediatR

**Status:** Aceito  
**Data:** 2024-01-01

## Contexto

Os serviços precisam de uma estrutura clara para separar operações de escrita (lançamentos) de operações de leitura (consultas), facilitando evolução e testabilidade independente.

## Decisão

Adotar o padrão **CQRS (Command Query Responsibility Segregation)** implementado via **MediatR** dentro de cada serviço. Commands alteram estado; Queries apenas leem.

## Estrutura

```
Commands/
  CreateEntryCommand.cs      → IRequest<Result<Guid>>
  CreateEntryCommandHandler  → IRequestHandler
  CreateEntryCommandValidator → AbstractValidator (FluentValidation)

Queries/
  GetEntriesByDateQuery.cs   → IRequest<Result<IEnumerable<EntryDto>>>
  GetEntriesByDateQueryHandler
```

## Benefícios

- Handlers são independentes e altamente testáveis (sem dependência de HTTP)
- Validação declarativa com FluentValidation via pipeline behavior
- Fácil adição de cross-cutting concerns (logging, performance tracking) via MediatR pipeline
- Separação clara de intenção (comando vs. consulta)

## Alternativas Rejeitadas

- **Service layer tradicional**: Mistura leitura e escrita no mesmo serviço. Tende a crescer em responsabilidades.
- **CQRS com bancos separados (read/write)**: Desnecessário para o volume atual. Complexidade sem benefício proporcional.
