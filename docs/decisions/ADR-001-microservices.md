# ADR-001: Arquitetura de Microservices

**Status:** Aceito  
**Data:** 2024-01-01  
**Contexto:** Sistema de controle de fluxo de caixa para comerciante.

## Contexto

O sistema precisa expor dois serviços com responsabilidades distintas:
1. Controle de lançamentos (débitos/créditos)
2. Consolidado diário

Um requisito não-funcional explícito é que o serviço de lançamentos **não deve ficar indisponível** caso o serviço de consolidado falhe.

## Decisão

Adotar arquitetura de **microservices** com dois serviços independentes, cada um com seu próprio banco de dados (Database per Service pattern).

## Alternativas Consideradas

| Alternativa | Motivo da Rejeição |
|---|---|
| Monolito | Acoplamento entre lançamentos e consolidado. Um deploy afeta ambas as funcionalidades. Não atende o requisito de isolamento de falhas. |
| Monolito Modular | Melhora organização mas ainda compartilha processo e banco. Falha em processo afeta ambos. |
| Microservices | **Escolhido.** Cada serviço é implantado, escalado e falha de forma independente. |

## Consequências

**Positivo:**
- Isolamento de falhas (requisito atendido)
- Escalabilidade independente
- Deploy independente
- Evolução independente de schema

**Negativo:**
- Maior complexidade operacional
- Necessidade de mensageria para comunicação assíncrona
- Consistência eventual (aceitável para consolidado diário)
