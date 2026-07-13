# CashFlow — Sistema de Controle de Fluxo de Caixa

Solução para o desafio técnico de Arquiteto de Soluções. Sistema event-driven de microservices em .NET 8 para controle de lançamentos financeiros e consolidado diário.

## Arquitetura

```
Comerciante
    │
    ▼ HTTPS
┌─────────────────────────────────────────────────────────────────┐
│  API Gateway (YARP)  :8000                                       │
│  • JWT validation       • Rate limiting (50 req/s token bucket)  │
│  • CORS                 • Security headers                        │
│  • Correlation ID       • Routing                                 │
└──────────────┬──────────────────────────┬───────────────────────┘
               │                          │
         /api/entries/*           /api/consolidated/*
               │                          │
               ▼                          ▼
  ┌────────────────────┐      ┌───────────────────────┐
  │  Entries Service   │      │  Consolidated Service  │
  │  :5001             │      │  :5002                 │
  │  Clean Architecture│      │  Consumer + CQRS       │
  └────────────┬───────┘      └───────────┬────────────┘
               │                          │
        INSERT ▼ events published         │ consumes events
       ┌────────────┐   AMQP   ┌─────────┴──────────┐
       │ PostgreSQL │          │     RabbitMQ         │
       │ (entries)  │─────────▶│  entry-created queue │
       └────────────┘          └─────────┬────────────┘
                                         │ consumed by
                                ┌────────▼──────────┐
                                │ PostgreSQL         │ ◄── Redis Cache
                                │ (consolidated)     │
                                └───────────────────┘
```

**Requisito crítico atendido**: O Entries Service **não depende** do Consolidated. Comunicação é 100% assíncrona via RabbitMQ. Se o Consolidated cair, o Entries continua operando normalmente.

## Quick Start

### Pré-requisitos

- [Docker](https://docs.docker.com/get-docker/) 24+
- [Docker Compose](https://docs.docker.com/compose/) v2

### 1. Configurar variáveis de ambiente

```bash
cp .env.example .env
# Editar .env — OBRIGATÓRIO trocar JWT_SECRET_KEY por uma chave forte
```

### 2. Subir todos os serviços

```bash
make up
# ou: docker compose up --build -d
```

Aguarde ~60 segundos para os serviços inicializarem (health checks).

### 3. Verificar status

```bash
make status
# ou: docker compose ps
```

### 4. Obter token JWT

```bash
curl -X POST http://localhost:8000/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"username": "comerciante1", "password": "qualquersenha"}'
```

Resposta:
```json
{
  "token": "eyJ...",
  "merchantId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "expiresAt": "2024-01-15T20:00:00Z"
}
```

### 5. Registrar lançamento

```bash
# Substitua <TOKEN> pelo token obtido acima
curl -X POST http://localhost:8000/api/entries \
  -H "Authorization: Bearer <TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 150.00,
    "currency": "BRL",
    "type": 1,
    "description": "Venda de produto",
    "entryDate": "2024-01-15"
  }'
```

`type`: `1` = Crédito, `2` = Débito

### 6. Consultar saldo consolidado

```bash
# Saldo do dia atual
curl http://localhost:8000/api/consolidated \
  -H "Authorization: Bearer <TOKEN>"

# Saldo de uma data específica
curl http://localhost:8000/api/consolidated/2024-01-15 \
  -H "Authorization: Bearer <TOKEN>"
```

## Executar Testes

```bash
# Instalar .NET 8 SDK primeiro:
# sudo snap install dotnet-sdk --classic

dotnet test CashFlow.sln --logger "console;verbosity=normal"
```

Testes incluem:
- **Unit Tests**: Domain entities, Value Objects, Command/Query Handlers, Validators
- **Integration Tests**: APIs completas com Testcontainers (PostgreSQL real, RabbitMQ real, Redis real)

## Observabilidade

| Painel | URL | Credenciais |
|---|---|---|
| Logs (Seq) | http://localhost:8888 | - |
| Traces (Jaeger) | http://localhost:16686 | - |
| RabbitMQ Management | http://localhost:15672 | cashflow / cashflow_pass |

## Documentação

```
docs/
├── architecture/
│   ├── context.md      # C4 Context Diagram
│   ├── container.md    # C4 Container Diagram
│   └── component.md    # C4 Component Diagram + Sequence Diagrams
├── security/
│   └── security-design.md   # Threat model, autenticação, OWASP
├── decisions/
│   ├── ADR-001-microservices.md
│   ├── ADR-002-async-messaging.md
│   ├── ADR-003-cqrs-mediatr.md
│   ├── ADR-004-jwt-auth.md
│   └── ADR-005-redis-cache.md
└── operations/
    └── runbook.md      # Deploy, monitoramento, escalabilidade, recuperação
```

## Estrutura do Repositório

```
src/
├── services/
│   ├── CashFlow.Entries/         # Serviço de Lançamentos
│   └── CashFlow.Consolidated/    # Serviço de Consolidado
├── shared/
│   ├── CashFlow.SharedKernel/    # Entity, ValueObject, DomainEvent, Result
│   └── CashFlow.EventBus/        # IntegrationEvent, IEventBus, MassTransit impl
└── gateway/
    └── CashFlow.Gateway/         # YARP Gateway + Auth endpoint

tests/
├── CashFlow.Entries.UnitTests/
├── CashFlow.Entries.IntegrationTests/
├── CashFlow.Consolidated.UnitTests/
└── CashFlow.Consolidated.IntegrationTests/

infrastructure/
└── docker/
    └── postgres/init-multiple-dbs.sh
```

## Stack Tecnológica

| Componente | Tecnologia | Justificativa |
|---|---|---|
| Runtime | .NET 8 / ASP.NET Core | LTS, performance, ecossistema |
| Padrão Arquitetural | Clean Architecture + CQRS | Testabilidade, separação de responsabilidades |
| Mensageria | RabbitMQ + MassTransit | Desacoplamento, retry automático, portabilidade |
| Banco de Dados | PostgreSQL 16 | ACID, confiabilidade, suporte EF Core excelente |
| Cache | Redis 7 | Performance, suporte distributed cache nativo .NET |
| API Gateway | YARP | Native .NET, flexível, rate limiting integrado |
| Auth | JWT Bearer (HMAC-SHA256) | Stateless, escalável horizontalmente |
| Observabilidade | Serilog + Seq + OpenTelemetry + Jaeger | Stack completo de logs, traces e métricas |
| Testes | xUnit + FluentAssertions + Moq + Testcontainers | Testes realistas com containers reais |

## Decisões Arquiteturais

Ver [ADRs em docs/decisions/](docs/decisions/).

## Segurança

Ver [docs/security/security-design.md](docs/security/security-design.md).

## Operação

Ver [docs/operations/runbook.md](docs/operations/runbook.md).
