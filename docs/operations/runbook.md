# Operations Runbook

## 1. Deploy

### Pré-requisitos

- Docker 24+ e Docker Compose v2
- .NET 8 SDK (para build local ou CI)

### Deploy Completo (todos os serviços)

```bash
# 1. Copiar e configurar variáveis de ambiente
cp .env.example .env
# Editar .env com um JWT_SECRET_KEY forte (min 32 chars)

# 2. Build e subir todos os containers
make up
# ou: docker compose up --build -d

# 3. Verificar status
make status
# ou: docker compose ps
```

### Deploy apenas infraestrutura

```bash
make infra-only
# Sobe: postgres, rabbitmq, redis, seq, jaeger
```

### Atualizar um serviço específico

```bash
docker compose up --build -d entries
docker compose up --build -d consolidated
docker compose up --build -d gateway
```

## 2. Portas Expostas

| Serviço | Porta Host | Descrição |
|---|---|---|
| API Gateway | 8000 | Ponto de entrada principal — única porta acessível externamente |
| PostgreSQL | 5432 | Banco de dados |
| RabbitMQ AMQP | 5672 | Mensageria |
| RabbitMQ Management | 15672 | Painel web (cashflow/cashflow_pass — dev) |
| Redis | 6379 | Cache |
| Seq | 8888 | Painel de logs |
| Jaeger UI | 16686 | Painel de traces |

> **Nota de segurança**: `Entries` (8080 interno) e `Consolidated` (8080 interno) não expõem portas no host. Todo tráfego externo passa obrigatoriamente pelo Gateway. Isso garante que JWT validation e rate limiting não podem ser contornados.

## 3. Health Checks

```bash
# Gateway (acesso externo)
curl http://localhost:8000/health/live

# Entries e Consolidated (acesso apenas interno — via docker exec ou rede interna)
docker exec cashflow-entries-1 wget -qO- http://localhost:8080/health/ready
docker exec cashflow-consolidated-1 wget -qO- http://localhost:8080/health/ready
```

Resposta esperada (ready): `{"status":"Healthy","checks":[{"name":"postgres","status":"Healthy"},...]}` 

## 4. Monitoramento

### Logs (Seq)

Acesse: http://localhost:8888

- Todos os serviços enviam logs estruturados JSON para o Seq
- Filtros úteis:
  - `Service = 'CashFlow.Entries'`
  - `@Level = 'Error'`
  - `CorrelationId = '<id>'`

### Traces (Jaeger)

Acesse: http://localhost:16686

- Serviço: `CashFlow.Entries`, `CashFlow.Consolidated`, `CashFlow.Gateway`
- Rastreamento de toda a cadeia de uma requisição usando o Correlation ID

### Métricas (OpenTelemetry)

Métricas OTEL estão habilitadas em `CashFlow.Entries` e `CashFlow.Consolidated` via `WithMetrics(AddAspNetCoreInstrumentation + AddRuntimeInstrumentation)`. As métricas são exportadas via OTLP para o mesmo endpoint de traces (Jaeger/Collector).

Métricas exportadas por padrão:
- `http.server.request.duration` (latência p50/p95/p99 por endpoint)
- `http.server.active_requests` (requisições em andamento)
- `process.runtime.dotnet.gc.*` (GC stats)
- `process.runtime.dotnet.thread_pool.*` (thread pool stats)

Para scraping Prometheus, adicionar `OpenTelemetry.Exporter.Prometheus.AspNetCore` e expor `/metrics`.

## 5. Escalabilidade

### Horizontal (múltiplas instâncias)

```bash
# Escalar Entries para 3 instâncias
docker compose up -d --scale entries=3

# Escalar Consolidated para 2 instâncias
docker compose up -d --scale consolidated=2
```

> **Nota**: O Gateway (YARP) roteia automaticamente entre instâncias (round-robin). Redis garante que o cache é compartilhado entre instâncias do Consolidated.

### Pico de 50 req/s

- Com 2 instâncias do Consolidated + Redis cache: suporta facilmente 50 req/s
- Token bucket no Gateway (50 tokens/s) garante que o sistema não recebe mais do que pode processar
- Fila de 10 absorve micro-picos sem perda

## 6. Rollback

```bash
# Reverter para imagem anterior (assumindo tag versionada)
docker compose down consolidated
docker tag cashflow-consolidated:v1.0.0 cashflow-consolidated:latest
docker compose up -d consolidated
```

## 7. Recuperação de Falhas

### Consolidated Service cai

- **Impacto no Entries**: **Nenhum.** Entries continua aceitando lançamentos normalmente.
- **Impacto no usuário**: Consultas de saldo retornam erro até o serviço se recuperar.
- **Recuperação automática**: MassTransit com retry policy (500ms, 1s, 2s, 5s). Após 4 tentativas, mensagem vai para Dead Letter Queue.
- **Idempotência**: mesmo que a mensagem seja reprocessada após restart, a tabela `processed_events` no Consolidated impede duplicação de saldo.
- **Ação**: `docker compose up -d consolidated`. Após subir, o consumer retoma processamento de mensagens pendentes. Mensagens na DLQ requerem redrive manual via RabbitMQ Management UI (http://localhost:15672 → fila `cashflow.consolidated.entry-created_error` → Move messages).

### RabbitMQ cai

- **Impacto**: Entries não consegue publicar eventos. Lançamentos são persistidos no banco **e** na tabela `outbox_messages` (Outbox, ADR-006) mas eventos não chegam ao Consolidated.
- **Ação**: Após RabbitMQ se recuperar, MassTransit reconecta automaticamente. O `OutboxProcessorBackgroundService` retoma o polling e publica todas as mensagens pendentes na próxima janela de 5s.
- **Verificar**: `SELECT COUNT(*) FROM outbox_messages WHERE processed_at IS NULL;` — deve chegar a zero após reconexão.

### PostgreSQL cai

- **Impacto**: Ambos os serviços param de funcionar.
- **Ação**: Restaurar backup. PostgreSQL com volumes Docker preserva dados entre restarts.

### Redis cai

- **Impacto**: Consolidated cai para busca direta no banco (cache miss em 100%). Performance degrada mas funcionalidade mantida.
- **Ação**: `docker compose up -d redis`. Cache se popula automaticamente com uso normal.

## 8. Backup

```bash
# Backup PostgreSQL
docker exec cashflow-postgres-1 pg_dump -U cashflow cashflow_entries > backup_entries_$(date +%Y%m%d).sql
docker exec cashflow-postgres-1 pg_dump -U cashflow cashflow_consolidated > backup_consolidated_$(date +%Y%m%d).sql
```

## 9. Alertas Recomendados (Produção)

| Alerta | Condição | Ação |
|---|---|---|
| Entries indisponível | health/live retorna 5xx por > 30s | Reiniciar container. Verificar logs no Seq. |
| Erro rate > 5% | Taxa de 5xx > 5% em 5 min | Verificar logs. Possível bug em deploy recente. |
| DLQ com mensagens | Dead Letter Queue não vazia | Investigar consumer do Consolidated. |
| Outbox com mensagens presas | `processed_at IS NULL` por > 5 min | Verificar conectividade com RabbitMQ. Ver logs do `OutboxProcessorBackgroundService`. |
| Outbox com alto retry_count | `retry_count > 5` em alguma mensagem | Investigar `error` na linha. Possível incompatibilidade de tipo de evento. |
| Memória Redis > 80% | Redis maxmemory quase atingido | Aumentar `maxmemory` ou adicionar instância. |
| Lag do consumer RabbitMQ alto | Fila > 1000 mensagens | Escalar instâncias do Consolidated. |

## 10. Monitoramento do Outbox

O `OutboxProcessorBackgroundService` roda no processo do `Entries Service` e faz polling da tabela `outbox_messages` a cada 5 segundos. Logs emitidos no Seq com filtro `Service = 'CashFlow.Entries'` e `@Message` contendo `outbox`.

### Comandos de diagnóstico

```bash
# Conectar ao banco de entries
docker exec -it cashflow-postgres-1 psql -U cashflow cashflow_entries

-- Mensagens pendentes (devem chegar a zero em condições normais)
SELECT COUNT(*) FROM outbox_messages WHERE processed_at IS NULL;

-- Mensagens com falhas de publicação
SELECT id, event_type, retry_count, error, occurred_at
FROM outbox_messages
WHERE retry_count > 0
ORDER BY occurred_at DESC
LIMIT 20;

-- Purge de mensagens processadas com mais de 30 dias
DELETE FROM outbox_messages
WHERE processed_at < NOW() - INTERVAL '30 days';
```

### Sintoma: Outbox não esvazia

1. Verificar se o `Entries Service` está rodando: `docker compose ps entries`
2. Verificar conectividade com RabbitMQ nos logs: `docker compose logs entries | grep -i outbox`
3. Se RabbitMQ estiver offline: após recovery, o processor retoma automaticamente na próxima janela de 5s

## 11. Pipeline de CI

O projeto usa GitHub Actions (`.github/workflows/ci.yml`) com 3 jobs:

| Job | Trigger | O que faz |
|---|---|---|
| `build` | push / PR em main, develop | `dotnet build` — falha rápida em erros de compilação |
| `unit-tests` | após build | Testa projetos `*.UnitTests` (sem infra externa) |
| `integration-tests` | após build (paralelo) | Testa projetos `*.IntegrationTests` (Testcontainers: Postgres + RabbitMQ) |

### Executar localmente

```bash
# Todos os testes
make test

# Apenas unitários
dotnet test CashFlow.sln --filter "FullyQualifiedName~UnitTests"

# Load test de performance (requer k6 instalado e stack rodando)
make load-test
```

