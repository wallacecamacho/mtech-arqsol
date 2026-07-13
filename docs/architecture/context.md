# C4 Model — Context Diagram

> Nível 1: Visão do sistema no contexto do negócio e dos atores externos.

```mermaid
C4Context
    title Sistema de Controle de Fluxo de Caixa — Context Diagram

    Person(merchant, "Comerciante", "Usuário do sistema. Registra lançamentos e consulta saldo consolidado.")
    Person_Ext(admin, "Administrador", "Gerencia usuários e monitora a operação.")

    System(cashflow, "CashFlow System", "Controla lançamentos financeiros (débitos e créditos) e consolida o saldo diário.")

    System_Ext(monitoring, "Stack de Observabilidade", "Seq (logs), Jaeger (traces via OTLP). Métricas Prometheus/Grafana: planejado.")
    System_Ext(idp, "Identity Provider (Gateway JWT)", "Emite e valida tokens de autenticação JWT.")

    Rel(merchant, cashflow, "Registra lançamentos e consulta saldo", "HTTPS / REST API")
    Rel(admin, cashflow, "Monitora health e logs", "HTTPS")
    Rel(cashflow, monitoring, "Envia logs e traces", "HTTP / gRPC (OTLP)")
    Rel(cashflow, idp, "Valida tokens JWT", "interno")
```

## Atores

| Ator | Descrição |
|---|---|
| Comerciante | Usuário principal. Registra débitos/créditos e consulta relatório diário. |
| Administrador | Acesso ao painel de observabilidade (Seq, Jaeger). |

## Sistemas Externos

| Sistema | Papel |
|---|---|
| Stack de Observabilidade | Logs estruturados (Seq), distributed tracing (Jaeger via OTLP/gRPC 4317). Métricas (Prometheus/Grafana) planejadas para fase 2. |
| Identity Provider | Emissão de JWT — no MVP o próprio Gateway emite o token (demo endpoint) |
