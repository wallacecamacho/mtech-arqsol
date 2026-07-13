# ADR-005: Cache Distribuído com Redis no Consolidated Service

**Status:** Aceito
**Data:** 2024-01-01
**Decisores:** Time de Arquitetura
**Depende de:** [ADR-001](ADR-001-microservices.md)

---

## Contexto

O `Consolidated Service` precisa suportar **50 requisições por segundo** com no máximo 5% de perda. O padrão de acesso é fortemente enviesado para leitura: comerciantes consultam o saldo do dia corrente repetidamente ao longo do dia. Sem cache, cada consulta geraria uma query no PostgreSQL, criando um gargalo desnecessário.

Além disso, saldos de dias anteriores são **imutáveis após o fechamento do dia** — um candidato ideal para cache de longa duração.

O serviço também é stateless e escala horizontalmente. Um cache in-process (`MemoryCache`) não funcionaria: cada instância teria seu próprio estado, gerando respostas inconsistentes entre réplicas.

## Drivers de Decisão

- **RNF1**: Suportar 50 req/s com ≤5% de perda
- **RNF2**: Cache deve funcionar com múltiplas instâncias (distribuído)
- **RNF3**: Invalidar cache após processamento de novo lançamento (consistência eventual correta)
- **RNF4**: TTL adaptativo: dado do dia corrente expira mais rápido que histórico

## Decisão

Usar **Redis 7** como cache distribuído via `IDistributedCache` (StackExchange.Redis). Padrão **Cache-Aside**:

```
GET cache[key]
  ├─ HIT  → retornar dado do Redis (< 1ms)
  └─ MISS → buscar no PostgreSQL → gravar no Redis com TTL → retornar
```

Invalidação ativa: após o consumer processar `EntryCreatedIntegrationEvent`, ele chama `RemoveAsync(cacheKey)` antes de retornar — garantindo que a próxima leitura reflita o estado atualizado.

### Esquema de Chaves e TTL

| Cenário | Chave | TTL | Justificativa |
|---|---|---|---|
| Saldo do dia corrente | `dailybalance:{merchantId}:{yyyy-MM-dd}` | **5 minutos** | Pode mudar com novos lançamentos a qualquer momento |
| Saldo de dias anteriores | `dailybalance:{merchantId}:{yyyy-MM-dd}` | **24 horas** | Imutável após fechamento do dia |

### Estimativa de Desempenho

Com 50 req/s e TTL de 5 min para o dia corrente:

| Request | Destino | Latência |
|---|---|---|
| 1º request por janela de 5 min | PostgreSQL (cache miss) | ~5–20ms |
| Próximos ~14.999 requests | Redis (cache hit) | < 1ms |
| **Hit rate esperado** | | **~99.9%** |

O banco recebe apenas ~0.2 req/s de leituras diretas — bem abaixo do limite.

## Consequências

**Positivo:**
- ✅ Suporta 50 req/s com hit rate de ~99.9% e latência sub-milissegundo no cache
- ✅ Cache distribuído: funciona corretamente com múltiplas instâncias do Consolidated
- ✅ Invalidação ativa garante consistência eventual rápida após novos lançamentos
- ✅ TTL adaptativo: histórico permanece cacheado por 24h sem risco de dados errados
- ✅ Abstração via `IDistributedCache`: facilita troca de Redis por outro provider sem alterar Application layer

**Negativo / Trade-offs:**
- ⚠️ Janela de inconsistência: entre a escrita no banco (pelo consumer) e a invalidação do cache, pode haver até 5 minutos de dado desatualizado **se** a invalidação falhar (mitigado: invalidação é feita antes de retornar)
- ⚠️ Redis como ponto adicional de infraestrutura — mitigado com Multi-AZ em produção
- ⚠️ Se Redis cair, Consolidated continua funcionando via fallback para o banco (degradação graciosa, não falha total)

## Comportamento em Caso de Falha do Redis

Como `IDistributedCache.GetStringAsync` lança exceção se Redis estiver inacessível, o handler captura e executa o fallback direto no banco. Isso garante **degradação graciosa**: o serviço funciona mais lento, mas não para.

## Alternativas Rejeitadas

| Alternativa | Motivo da Rejeição |
|---|---|
| `MemoryCache` (in-process) | Não distribuído: cada instância teria cache diferente, gerando inconsistência em scale-out |
| Sem cache | PostgreSQL sob 50 req/s é viável, mas cria ponto de pressão desnecessário |
| Memcached | Redis oferece persistência, pub/sub e melhor ecossistema com complexidade similar |

## Referências

- [Component Diagram — Consolidated Service](../architecture/component.md)
- [Runbook — Redis](../operations/runbook.md)
- [ADR-002: Comunicação Assíncrona](ADR-002-async-messaging.md)
