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
| API Gateway | 8000 | Ponto de entrada principal |
| Entries Service | 5001 | Direto (dev only) |
| Consolidated Service | 5002 | Direto (dev only) |
| PostgreSQL | 5432 | Banco de dados |
| RabbitMQ AMQP | 5672 | Mensageria |
| RabbitMQ Management | 15672 | Painel web (cashflow/cashflow_pass — dev) |
| Redis | 6379 | Cache |
| Seq | 8888 | Painel de logs |
| Jaeger UI | 16686 | Painel de traces |

## 3. Health Checks

```bash
# Gateway
curl http://localhost:8000/health/live

# Entries
curl http://localhost:5001/health/ready

# Consolidated
curl http://localhost:5002/health/ready
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

### Métricas

> **Planejado (fase 2).** Métricas OpenTelemetry ainda não estão habilitadas nesta versão. Para ativar:

1. Adicionar `OpenTelemetry.Exporter.Prometheus.AspNetCore` e `builder.Services.AddOpenTelemetry().WithMetrics(...)` em cada serviço
2. Configurar scrape no `prometheus.yml`
3. Importar dashboard Grafana ASP.NET Core

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
- **Ação**: `docker compose up -d consolidated`. Após subir, o consumer retoma processamento de mensagens pendentes. Mensagens na DLQ requerem redrive manual via RabbitMQ Management UI (http://localhost:15672 → fila `cashflow.consolidated.entry-created_error` → Move messages).

### RabbitMQ cai

- **Impacto**: Entries não consegue publicar eventos. Lançamentos são persistidos no banco mas eventos não são publicados.
- **Ação**: Após RabbitMQ se recuperar, MassTransit reconecta automaticamente.
- **Reconciliação**: Se necessário, job de reconciliação pode re-publicar eventos baseado na tabela de entries.

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
| Memória Redis > 80% | Redis maxmemory quase atingido | Aumentar `maxmemory` ou adicionar instância. |
| Lag do consumer RabbitMQ alto | Fila > 1000 mensagens | Escalar instâncias do Consolidated. |
