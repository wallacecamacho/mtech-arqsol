# ADR-005: Redis para Cache do Consolidated Service

**Status:** Aceito  
**Data:** 2024-01-01

## Contexto

O Consolidated Service precisa suportar **50 requisições por segundo** com no máximo 5% de perda. O banco de dados PostgreSQL sob esse volume sem cache seria um gargalo. Além disso, saldos de dias anteriores (imutáveis após processamento) são excelentes candidatos a cache longo.

## Decisão

Usar **Redis 7** como cache distribuído via `IDistributedCache` (StackExchange.Redis). Estratégia **Cache-Aside**: busca no cache primeiro; se miss, busca no banco e popula cache.

## Estratégia de TTL

| Cenário | TTL | Justificativa |
|---|---|---|
| Saldo do dia atual | 5 minutos | Pode mudar com novos lançamentos |
| Saldo de dias anteriores | 24 horas | Imutável após fechamento do dia |

## Estimativa de Hit Rate

Com 50 req/s e TTL de 5 min para o dia corrente:
- Primeiro request: miss (busca no banco)
- Próximos ~14.999 requests em 5 min: hit (resposta em <1ms)
- **Hit rate esperado: ~99.9%**

Isso garante que o banco suporte apenas ~0.2 req/s de leituras diretas, muito abaixo do limite.

## Invalidação de Cache

Após o consumer processar um `EntryCreatedIntegrationEvent`, o cache da data correspondente é invalidado para que a próxima leitura reflita o estado atualizado.

## Alternativas Rejeitadas

- **MemoryCache (in-process)**: Não funciona em múltiplas instâncias do serviço (scale-out).
- **Sem cache**: PostgreSQL sob 50 req/s é viável mas cria ponto de pressão. Cache garante margem de segurança.
- **Memcached**: Redis oferece mais features (persistence, pub/sub) com complexidade similar.
