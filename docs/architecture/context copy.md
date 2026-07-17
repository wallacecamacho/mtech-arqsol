# C4 Model — Context Diagram

> **Nível 1 — Business Context:** Visão de mais alto nível do sistema. Mostra quem usa o sistema, o que o sistema faz e com quais sistemas externos ele se integra. Não detalha tecnologias internas.

---

## Problema de Negócio

Comerciantes precisam de controle financeiro diário confiável: registrar entradas e saídas (débitos/créditos) e consultar o saldo consolidado a qualquer momento. O sistema deve garantir que o registro de lançamentos continue funcionando mesmo que a consolidação esteja temporariamente indisponível.

---

## Diagrama

```mermaid
C4Context
    title Sistema de Controle de Fluxo de Caixa — Context Diagram

    Person(merchant, "Comerciante", "Usuário principal do sistema. Registra lançamentos financeiros (débitos e créditos) e consulta o saldo consolidado diário via API REST.")
    Person_Ext(admin, "Administrador", "Equipe de operações. Monitora saúde dos serviços, analisa logs e traces, responde a alertas.")

    System(cashflow, "CashFlow System", "Plataforma de controle de fluxo de caixa. Permite registrar lançamentos financeiros e consultar o saldo diário consolidado por comerciante. Garante disponibilidade do registro mesmo sob falha parcial.")

    System_Ext(monitoring, "Stack de Observabilidade", "Coleta logs estruturados (Seq), distributed traces (Jaeger via OTLP/gRPC) e expõe dashboards operacionais. Métricas Prometheus/Grafana planejadas para fase 2.")
    System_Ext(idp, "Identity Provider", "Emite e valida tokens JWT (HMAC-SHA256). No MVP, o próprio API Gateway expõe um endpoint de autenticação simplificado. Em produção, substituir por Keycloak ou Azure AD B2C.")

    Rel(merchant, cashflow, "Registra lançamentos e consulta saldo consolidado", "HTTPS / REST API")
    Rel(admin, cashflow, "Monitora health checks, logs e traces", "HTTPS")
    Rel(cashflow, monitoring, "Envia logs estruturados e distributed traces", "HTTP 5341 (Seq) / gRPC 4317 (OTLP)")
    Rel(cashflow, idp, "Autentica requisições via JWT Bearer", "interno")
    Rel(admin, monitoring, "Consulta logs, traces e dashboards", "HTTPS")
```

---

## Atores

| Ator | Tipo | Responsabilidades |
|---|---|---|
| **Comerciante** | Usuário interno | Registra débitos e créditos via API. Consulta o relatório de saldo diário consolidado. Autenticado via JWT Bearer. |
| **Administrador** | Usuário externo | Monitora a saúde operacional do sistema. Acessa Seq para análise de logs e Jaeger para rastreamento de requisições. Não interage com a API de negócio. |

---

## Sistemas Externos

| Sistema | Papel | Tecnologia (dev) | Tecnologia (prod) |
|---|---|---|---|
| **Stack de Observabilidade** | Logs estruturados JSON, distributed tracing ponta-a-ponta, dashboards operacionais. | Seq + Jaeger (containers Docker) | CloudWatch + X-Ray (AWS) / Log Analytics + Application Insights (Azure) |
| **Identity Provider** | Emissão de tokens JWT assinados (HMAC-SHA256). Valida issuer, audience e expiração. | Endpoint `/api/auth/token` no próprio Gateway (demo) | Keycloak self-hosted ou Azure AD B2C (OAuth2 + PKCE) |

---

## Requisitos Não-Funcionais Capturados neste Nível

| Requisito | Decisão |
|---|---|
| **Disponibilidade do registro** | Registro de lançamentos não depende do serviço de consolidação (assíncrono via mensageria) |
| **Isolamento de dados por comerciante** | `merchantId` extraído do JWT — nunca do body da requisição |
| **Autenticação** | JWT Bearer validado no Gateway e re-validado nos serviços internos (defense in depth) |
| **Observabilidade** | Correlation ID propagado em todas as requisições; logs e traces centralizados |
| **Escalabilidade** | Cada serviço escala horizontalmente de forma independente |

---

## Navegação

| Próximo nível | Arquivo |
|---|---|
| Container Diagram (serviços internos e infra) | [container.md](container.md) |
| Component Diagram (componentes por serviço) | [component.md](component.md) |
| Cloud Architecture (AWS) | [cloud.md](cloud.md) |
| Cloud Architecture (Azure) | [cloud-azure.md](cloud-azure.md) |
