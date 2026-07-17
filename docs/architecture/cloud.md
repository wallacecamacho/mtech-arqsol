# Cloud Architecture — CashFlow System

> Mapeamento da solução para infraestrutura AWS production-ready.  
> Referência: [C4 Container Diagram](container.md) · [Versão Azure](cloud-azure.md)

---


## 1. Visão Geral da Infraestrutura AWS

```mermaid
flowchart TB
    USER(["Comerciante"])

    subgraph EDGE["Edge"]
        R53["Route 53"]
        WAF["AWS WAF"]
        ALB["ALB + ACM"]
    end

    subgraph ECS["ECS Fargate  —  Private App Subnet"]
        GW["API Gateway\n.NET 8 / YARP"]
        ENT["Entries Service\n.NET 8"]
        CON["Consolidated Service\n.NET 8"]
    end

    subgraph DATA["Managed Data  —  Private Data Subnet"]
        RDS_E[("RDS PostgreSQL\ncashflow_entries")]
        RDS_C[("RDS PostgreSQL\ncashflow_consolidated")]
        MQ[["Amazon MQ\nRabbitMQ"]]
        REDIS[("ElastiCache\nRedis")]
    end

    subgraph OBS["Observabilidade"]
        CW["CloudWatch\nLogs + Alarms"]
        XRAY["X-Ray\nTracing"]
    end

    subgraph SEC["Segurança"]
        SM["Secrets Manager"]
        KMS["KMS"]
    end

    USER -->|HTTPS| R53 --> WAF --> ALB --> GW
    GW -->|proxy| ENT
    GW -->|proxy| CON
    ENT --> RDS_E
    ENT -->|publish| MQ
    MQ -->|consume| CON
    CON --> RDS_C
    CON --> REDIS
    ECS -->|logs/traces| OBS
    ECS -->|secrets/keys| SEC
    DATA -.->|encrypted| KMS
```

---

## 1.1 Visão Geral da Infraestrutura AWS

```mermaid
flowchart TB
    subgraph INTERNET["Internet"]
        USER["Comerciante\n(Browser / App)"]
    end

    subgraph AWS["AWS Cloud — Region us-east-1"]

        subgraph EDGE["Edge / Ingress"]
            R53["Route 53\nDNS + Health Routing"]
            WAF["AWS WAF\nOWASP Rules + Rate Limit"]
            ALB["Application Load Balancer\nTLS Termination (ACM)"]
        end

        subgraph VPC["VPC  10.0.0.0/16"]

            subgraph PUB["Public Subnets  10.0.1.0/24 · 10.0.2.0/24"]
                NAT["NAT Gateway"]
            end

            subgraph APP["Private App Subnets  10.0.11.0/24 · 10.0.12.0/24"]
                subgraph ECS_GW["ECS Fargate — Gateway Service"]
                    GW1["Task: cashflow-gateway\n.NET 8 / YARP\n:8080"]
                    GW2["Task: cashflow-gateway\n.NET 8 / YARP\n:8080"]
                end

                subgraph ECS_ENT["ECS Fargate — Entries Service"]
                    ENT1["Task: cashflow-entries\n.NET 8\n:8080"]
                    ENT2["Task: cashflow-entries\n.NET 8\n:8080"]
                end

                subgraph ECS_CON["ECS Fargate — Consolidated Service"]
                    CON1["Task: cashflow-consolidated\n.NET 8\n:8080"]
                    CON2["Task: cashflow-consolidated\n.NET 8\n:8080"]
                end
            end

            subgraph DATA["Private Data Subnets  10.0.21.0/24 · 10.0.22.0/24"]
                subgraph RDS["Amazon RDS PostgreSQL 16"]
                    RDS_ENT_P["Primary\ncashflow_entries"]
                    RDS_ENT_R["Read Replica\ncashflow_entries"]
                    RDS_CON_P["Primary\ncashflow_consolidated"]
                    RDS_CON_R["Read Replica\ncashflow_consolidated"]
                end

                AMQP["Amazon MQ\nRabbitMQ 3\n(Multi-AZ)"]

                subgraph CACHE["ElastiCache Redis 7"]
                    RD_P["Primary\nAZ-a"]
                    RD_R["Replica\nAZ-b"]
                end
            end
        end

        subgraph DEVOPS["DevOps & CI/CD"]
            ECR["ECR\nContainer Registry"]
            CODEPIPELINE["CodePipeline\nCI/CD"]
            CODEBUILD["CodeBuild\ndotnet build + test"]
        end

        subgraph OBSERVABILITY["Observabilidade"]
            CW["CloudWatch\nLogs + Metrics + Alarms"]
            XRAY["AWS X-Ray\nDistributed Tracing"]
            CWD["CloudWatch Dashboard\nAPM Metrics"]
        end

        subgraph SECURITY["Segurança & Config"]
            SM["Secrets Manager\nJWT_SECRET_KEY\nDB passwords"]
            SSM["SSM Parameter Store\nConfigs não-sensíveis"]
            KMS["KMS\nCriptografia em repouso"]
        end

    end

    USER -->|HTTPS| R53
    R53 --> WAF
    WAF --> ALB
    ALB -->|"/api/entries\n/api/consolidated\n/api/auth"| ECS_GW

    GW1 & GW2 -->|"Proxy HTTP"| ECS_ENT
    GW1 & GW2 -->|"Proxy HTTP"| ECS_CON

    ENT1 & ENT2 -->|"TCP 5432"| RDS_ENT_P
    ENT1 & ENT2 -->|"AMQP"| AMQP
    CON1 & CON2 -->|"TCP 5432"| RDS_CON_P
    CON1 & CON2 -->|"AMQP consume"| AMQP
    CON1 & CON2 -->|"TCP 6379"| RD_P

    ECS_GW & ECS_ENT & ECS_CON -->|"OTLP gRPC"| XRAY
    ECS_GW & ECS_ENT & ECS_CON -->|"Logs JSON"| CW

    APP -->|"Saída internet"| NAT

    CODEPIPELINE --> CODEBUILD --> ECR
    ECR --> ECS_GW & ECS_ENT & ECS_CON

    ECS_GW & ECS_ENT & ECS_CON -->|"GetSecretValue"| SM
    ECS_GW & ECS_ENT & ECS_CON -->|"GetParameter"| SSM
    RDS & CACHE -->|"Encrypted"| KMS
```

---

## 2. Diagrama de Rede (Security Groups)

```mermaid
flowchart LR
    subgraph SG_ALB["SG: alb-sg"]
        direction TB
        A1["Ingress: 443 TCP — 0.0.0.0/0"]
        A2["Egress: 8080 TCP → app-sg"]
    end

    subgraph SG_APP["SG: app-sg"]
        direction TB
        B1["Ingress: 8080 TCP ← alb-sg"]
        B2["Egress: 5432 TCP → data-sg"]
        B3["Egress: 5671 TCP → data-sg  (AMQP TLS)"]
        B4["Egress: 6379 TCP → data-sg"]
        B5["Egress: 443 TCP → 0.0.0.0/0  (ECR, SM, SSM)"]
        B6["Egress: 2000 UDP → 0.0.0.0/0  (X-Ray daemon)"]
    end

    subgraph SG_DATA["SG: data-sg"]
        direction TB
        C1["Ingress: 5432 TCP ← app-sg"]
        C2["Ingress: 5671 TCP ← app-sg"]
        C3["Ingress: 6379 TCP ← app-sg"]
    end

    SG_ALB -->|"traffic"| SG_APP
    SG_APP -->|"traffic"| SG_DATA
```

---

## 3. Mapeamento: Local → AWS

| Componente Local (Docker Compose) | Serviço AWS | Justificativa |
|---|---|---|
| `cashflow-gateway` (ECS container) | **ECS Fargate** (Task) | Serverless containers — sem EC2 para gerenciar |
| `cashflow-entries` (ECS container) | **ECS Fargate** (Task) | Escala horizontal independente |
| `cashflow-consolidated` (ECS container) | **ECS Fargate** (Task) | Escala independente do Entries |
| PostgreSQL (Docker) | **Amazon RDS PostgreSQL 16** Multi-AZ | HA automático, backups gerenciados, PITR |
| RabbitMQ (Docker) | **Amazon MQ** (RabbitMQ) | Broker gerenciado com failover automático |
| Redis (Docker) | **ElastiCache for Redis** cluster mode | Multi-AZ, replicação automática |
| Seq (Docker) | **CloudWatch Logs** | Serviço gerenciado; Insights = SQL-like queries |
| Jaeger (Docker) | **AWS X-Ray** | Tracing nativo com console integrado ao console AWS |
| `.env` secrets | **Secrets Manager** | Rotação automática, auditoria via CloudTrail |
| `appsettings.json` config | **SSM Parameter Store** | Hierarquia `/cashflow/{env}/{service}/config` |
| Docker Hub images | **ECR** (Elastic Container Registry) | Private registry dentro da mesma região |

---

## 4. Estratégia de Escalonamento

```mermaid
flowchart TD
    subgraph AUTOSCALING["Auto Scaling Strategy"]
        direction TB

        subgraph GATEWAY_AS["Gateway Service"]
            GW_MIN["min: 2 tasks"]
            GW_MAX["max: 10 tasks"]
            GW_CPU["Scale-out: CPU > 60%\nScale-in: CPU < 30%"]
        end

        subgraph ENTRIES_AS["Entries Service"]
            ENT_MIN["min: 2 tasks"]
            ENT_MAX["max: 20 tasks"]
            ENT_CPU["Scale-out: CPU > 60%\nScale-in: CPU < 30%"]
            ENT_REQ["Scale-out: ReqCount > 1000/min"]
        end

        subgraph CONSOL_AS["Consolidated Service"]
            CON_MIN["min: 2 tasks"]
            CON_MAX["max: 10 tasks"]
            CON_QUEUE["Scale-out: QueueDepth > 500 msgs\nScale-in: QueueDepth < 100"]
            CON_CPU["Scale-out: CPU > 70%"]
        end
    end

    LOAD["ALB Request Count\nCloudWatch Alarm"] --> ENTRIES_AS
    QUEUE["Amazon MQ\nQueue Depth Alarm"] --> CONSOL_AS
    CPU_ALARM["ECS CPU Alarm"] --> GATEWAY_AS
```

### SLA de Escalabilidade

| Métrica | Threshold | Ação |
|---|---|---|
| Entries CPU > 60% | 2 minutos | +2 tasks (cooldown 60s) |
| Entries Request Count > 1000/min | imediato | +2 tasks |
| Consolidated Queue Depth > 500 | 1 minuto | +2 tasks |
| Gateway CPU > 60% | 2 minutos | +2 tasks |
| Qualquer serviço CPU < 30% | 5 minutos | -1 task (scale-in conservador) |

---

## 5. CI/CD Pipeline

```mermaid
flowchart LR
    subgraph REPO["GitHub"]
        PR["Pull Request"]
        MAIN["Branch: main"]
    end

    subgraph PIPELINE["AWS CodePipeline"]
        SRC["Source\nCodeStar → GitHub"]
        BUILD["CodeBuild\ndotnet restore\ndotnet build\ndotnet test\ndotnet publish"]
        IMG["Docker Build\ndocker build\ndocker push ECR"]
        DEP_STG["Deploy → Staging\nECS Blue/Green"]
        SMOKE["Smoke Tests\nCodeBuild"]
        DEP_PRD["Deploy → Production\nECS Blue/Green\n(manual approval)"]
    end

    subgraph ECS_ENV["Ambientes"]
        STAGING["ECS: cashflow-staging"]
        PROD["ECS: cashflow-prod"]
    end

    PR -->|"merge"| MAIN
    MAIN --> SRC --> BUILD --> IMG --> DEP_STG --> SMOKE
    SMOKE -->|"passed"| DEP_PRD
    DEP_STG --> STAGING
    DEP_PRD --> PROD
```

### Blue/Green Deployment (Zero-Downtime)

1. CodeDeploy cria um novo **Target Group** com as novas tasks (Green)
2. ALB roteia 10% do tráfego para Green (canary)
3. CloudWatch monitora por 5 minutos (error rate, latência p99)
4. Se saudável: 100% do tráfego vai para Green; Blue é destruído
5. Se alarm: rollback automático em < 60 segundos

---

## 6. Estratégia de Disaster Recovery

```mermaid
flowchart TD
    subgraph PRIMARY["Região Primária\nus-east-1"]
        ALB_P["ALB"]
        ECS_P["ECS Fargate\n(3 serviços)"]
        RDS_P["RDS Multi-AZ\nPrimary + Standby"]
        AMQP_P["Amazon MQ\nMulti-AZ"]
        REDIS_P["ElastiCache\nPrimary + Replica"]
    end

    subgraph SECONDARY["Região Secundária\nus-west-2  (Warm Standby)"]
        ALB_S["ALB\n(standby)"]
        ECS_S["ECS Fargate\nmin 1 task each"]
        RDS_S["RDS Read Replica\n→ promote on failover"]
        AMQP_S["Amazon MQ\n(standby)"]
        REDIS_S["ElastiCache\n(standby)"]
    end

    subgraph DNS["Route 53"]
        HC["Health Checks\n(30s interval)"]
        POLICY["Failover Routing Policy\nPrimary → Secondary"]
    end

    RDS_P -->|"async replication"| RDS_S
    REDIS_P -->|"cross-region replication"| REDIS_S

    HC -->|"monitor"| ALB_P
    HC -->|"failover trigger"| POLICY
    POLICY -->|"primary"| ALB_P
    POLICY -->|"failover"| ALB_S
```

| Tier | RTO | RPO | Estratégia |
|---|---|---|---|
| RDS (dados financeiros) | < 5 min | < 1 min | Multi-AZ sync standby + cross-region async replica |
| ECS Tasks | < 3 min | N/A (stateless) | Auto Scaling relança tasks na AZ sobrevivente |
| Amazon MQ | < 2 min | 0 (durable queues) | Multi-AZ com armazenamento durável |
| ElastiCache Redis | < 1 min | < 30s | Replicação automática + failover automático |
| Region failover total | < 15 min | < 5 min | Route 53 failover + warm standby |

---

## 7. Observabilidade em Produção

```mermaid
flowchart LR
    subgraph SERVICES["Serviços ECS"]
        GW["Gateway"]
        ENT["Entries"]
        CON["Consolidated"]
    end

    subgraph OBS["Stack de Observabilidade AWS"]
        subgraph LOGS["Logs"]
            CWL["CloudWatch Logs\n/cashflow/prod/gateway\n/cashflow/prod/entries\n/cashflow/prod/consolidated"]
            INSIGHTS["CloudWatch\nLogs Insights\n(SQL-like queries)"]
        end

        subgraph TRACES["Traces"]
            XRAY["AWS X-Ray\nService Map\nTrace Analysis"]
        end

        subgraph METRICS["Metrics & Alarms"]
            CWM["CloudWatch Metrics\nCustom Namespace:\nCashFlow/Business"]
            ALARMS["CloudWatch Alarms\n→ SNS → Email/PagerDuty"]
            DASH["CloudWatch Dashboard\nAPM Overview"]
        end
    end

    GW & ENT & CON -->|"JSON logs\nFireLens sidecar"| CWL
    GW & ENT & CON -->|"OTLP gRPC\n:2000"| XRAY
    CWL --> INSIGHTS
    CWM --> ALARMS
    CWM --> DASH
```

### Alarmes Críticos de Produção

| Alarme | Métrica | Threshold | Ação |
|---|---|---|---|
| `HighErrorRate` | ALB 5xx / Total | > 5% por 3 min | SNS → PagerDuty P1 |
| `HighLatency` | ALB TargetResponseTime p99 | > 2s por 5 min | SNS → PagerDuty P2 |
| `QueueDepthHigh` | AmazonMQ QueueSize | > 1000 msgs | SNS → PagerDuty P2 + Scale-out |
| `DBConnections` | RDS DatabaseConnections | > 80% max | SNS → PagerDuty P2 |
| `CacheEvictions` | ElastiCache Evictions | > 100/min | SNS → Slack |
| `TaskStopped` | ECS TaskCount | < min healthy | SNS → PagerDuty P1 |
| `DLQNotEmpty` | AmazonMQ DLQ depth | > 0 | SNS → Slack + ticket |

---

## 8. Estimativa de Custo (us-east-1 — 50 req/s pico)

| Serviço | Configuração | Custo/mês (estimado) |
|---|---|---|
| ECS Fargate — 3 serviços | 2–6 tasks × 0.5 vCPU / 1GB | ~$60–$120 |
| RDS PostgreSQL | db.t4g.medium Multi-AZ × 2 instâncias | ~$140 |
| Amazon MQ | mq.m5.large Multi-AZ | ~$200 |
| ElastiCache Redis | cache.t4g.small cluster | ~$30 |
| ALB | ~50M requisições/mês | ~$20 |
| ECR | ~3 imagens × 1GB | ~$3 |
| CloudWatch Logs | ~10GB/mês | ~$5 |
| Secrets Manager | 5 secrets | ~$2 |
| NAT Gateway | ~50GB | ~$25 |
| **Total estimado** | | **~$485–$545/mês** |

> Redução possível com Reserved Instances (RDS/ElastiCache 1 ano): ~35% de desconto → **~$320–$355/mês**

---

## 9. Checklist de Produção

- [ ] `JWT_SECRET_KEY` ≥ 32 chars armazenada no Secrets Manager (não em `.env`)
- [ ] RDS: encryption at rest habilitado (KMS CMK), backups automáticos 7 dias, PITR
- [ ] ElastiCache: `requirepass` configurado via Secrets Manager, encryption in-transit
- [ ] WAF: regras OWASP Core Rule Set + rate limit por IP (1000 req/5min)
- [ ] ALB: TLS 1.2+ policy `ELBSecurityPolicy-TLS13-1-2-2021-06`
- [ ] ECS Tasks: `readonlyRootFilesystem: true`, non-root user (`uid 1001`)
- [ ] VPC Flow Logs habilitados
- [ ] CloudTrail habilitado para auditoria de API calls
- [ ] Alertas de billing (Budget Alarm > $600/mês)
- [ ] Runbook de DLQ revisado (ver [runbook.md](../operations/runbook.md))
- [ ] Smoke tests após cada deploy (Blue/Green)
- [ ] Tags em todos os recursos: `Project=CashFlow`, `Env=prod`, `ManagedBy=terraform`

---

## 10. Evidência de RNF — Load Test em AWS

Executar os cenários k6 contra o ALB após deploy:

```bash
# 1. Configurar target AWS
export BASE_URL=https://api.cashflow.example.com   # ALB DNS / Route 53 alias

# 2. Smoke (sanidade rápida)
k6 run -e BASE_URL=$BASE_URL -e SCENARIO=smoke tests/load/k6-scenarios.js

# 3. Load (evidência RNF)
k6 run -e BASE_URL=$BASE_URL -e SCENARIO=load \
       --summary-export=tests/load/results/aws-load-$(date +%Y%m%d).json \
       tests/load/k6-scenarios.js
```

### Thresholds esperados

| Threshold | Valor esperado em AWS (ECS Fargate + ALB) |
|---|---|
| `http_req_failed < 0.05` | ~0.1% (ALB health checks filtram instâncias doentes) |
| `http_req_duration p(95) < 500ms` | ~120 ms (NIC Fargate + ElastiCache < 1ms) |
| Throughput sustentado | ~80 req/s com 2 tasks por serviço |

Relatório completo: [`docs/operations/rnf-throughput-evidence.md`](../operations/rnf-throughput-evidence.md)
