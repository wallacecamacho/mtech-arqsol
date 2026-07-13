# ADR-001: Arquitetura de Microservices

**Status:** Aceito
**Data:** 2024-01-01
**Decisores:** Time de Arquitetura
**Ticket de origem:** Desafio Técnico — Arquiteto de Soluções

---

## Contexto

O sistema de controle de fluxo de caixa precisa atender a dois casos de uso com ciclos de vida, cargas e padrões de acesso radicalmente diferentes:

1. **Controle de lançamentos** — operações de escrita frequentes (débitos/créditos), latência baixa, disponibilidade crítica.
2. **Consolidado diário** — operações de leitura intensas com pico de 50 req/s, dados calculados a partir de eventos, tolerante a consistência eventual.

Há um **requisito não-funcional explícito**: *"O serviço de controle de lançamentos não deve ficar indisponível caso o serviço de consolidado diário falhe."* Isso elimina qualquer arquitetura que acople os dois em um mesmo processo ou dependência síncrona.

## Drivers de Decisão

- **RF1**: Registrar débitos e créditos por comerciante
- **RF2**: Consultar saldo consolidado diário
- **RNF1**: Entries não pode ser impactado por falha no Consolidated
- **RNF2**: Escalabilidade independente por serviço (picos de consulta não afetam escrita)
- **RNF3**: Possibilidade de evolução de schema sem coordenação entre times

## Alternativas Consideradas

| Alternativa | Isolamento de falhas | Escala independente | Complexidade operacional | Decisão |
|---|---|---|---|---|
| **Monolito simples** | [x] Um processo, falha compartilhada | [x] Escala tudo junto | [OK] Mínima | Rejeitado |
| **Monolito Modular** | [!] Mesmo processo, módulos isolados | [x] Escala tudo junto | [OK] Baixa | Rejeitado |
| **Microservices** | [OK] Processos e bancos independentes | [OK] Cada serviço escala isolado | [!] Média | **Escolhido** |

## Decisão

Adotar **arquitetura de microservices** com dois serviços independentes:

- `CashFlow.Entries` — gerencia lançamentos, banco `cashflow_entries`
- `CashFlow.Consolidated` — consolida saldo diário, banco `cashflow_consolidated`
- `CashFlow.Gateway` — ponto de entrada único (JWT, rate limiting, roteamento)

Aplicado o padrão **Database per Service**: cada serviço é dono exclusivo do seu schema. Nenhum serviço acessa o banco do outro diretamente.

A comunicação entre serviços é **assíncrona via RabbitMQ** (ver ADR-002), garantindo que a indisponibilidade do Consolidated não propague para o Entries.

## Consequências

**Positivo:**
- [OK] Requisito RNF1 atendido: Entries opera normalmente mesmo com Consolidated offline
- [OK] Entries e Consolidated escalam horizontalmente de forma independente
- [OK] Deploy de um serviço não requer restart do outro
- [OK] Schema de cada banco evolui sem coordenação
- [OK] Cada serviço pode ser versionado, testado e monitorado de forma isolada

**Negativo / Trade-offs:**
- [!] Maior complexidade operacional (3 serviços, 2 bancos, 1 broker)
- [!] Consistência eventual: saldo consolidado pode ter atraso de segundos após um lançamento
- [!] Distributed tracing necessário para rastrear fluxos cross-service (mitigado com Jaeger + Correlation ID)
- [!] Mais infra para manter em desenvolvimento local (mitigado com Docker Compose)

## Notas de Implementação

- Cada serviço tem seu próprio `Dockerfile`, `appsettings.json` e migração EF Core
- O Gateway é implementado com **YARP** (Yet Another Reverse Proxy) — roteamento baseado em configuração, sem código de proxy manual
- Comunicação interna via HTTP simples dentro da rede Docker privada (sem TLS interno no MVP)

## Referências

- [ADR-002: Comunicação Assíncrona via RabbitMQ](ADR-002-async-messaging.md)
- [Container Diagram](../architecture/container.md)
