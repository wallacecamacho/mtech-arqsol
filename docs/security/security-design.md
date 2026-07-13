# Security Design Document

## 1. Visão Geral

A segurança da solução CashFlow é implementada em camadas (defense in depth), cobrindo desde a borda (API Gateway) até o banco de dados.

```
Internet → [TLS] → API Gateway → [JWT Validation] → [Rate Limit] → Internal Services → [DB Encryption at rest]
```

## 2. Autenticação

### JWT Bearer Token

- **Algoritmo**: HMAC-SHA256 com chave simétrica ≥ 32 bytes
- **Chave**: Armazenada como variável de ambiente (`JWT_SECRET_KEY`) em produção. Os arquivos `appsettings.json` contêm **valores de exemplo** para desenvolvimento local — nunca usar em produção.
- **Expiração**: 8 horas. ClockSkew = 0.
- **Claims obrigatórias**: `sub` (merchantId), `iss`, `aud`, `exp`
- **Validação dupla**: Gateway valida e propaga. Serviços internos também validam independentemente (defense in depth).

### Fluxo de Autenticação

```
1. Comerciante → POST /api/auth/token {username, password}
2. Gateway valida credenciais (demo: qualquer não-vazio; produção: IdP)
3. Gateway emite JWT assinado (sub = merchantId UUID)
4. Comerciante inclui JWT em todas as requisições: Authorization: Bearer <token>
5. Gateway valida JWT antes de proxiar
6. Serviço interno re-valida JWT (defense in depth)
7. MerchantId extraído do claim `sub` — isolamento de dados por tenant
```

### Produção

Em produção, o endpoint `/api/auth/token` deve ser substituído por um Identity Provider dedicado:
- **Recomendado**: Keycloak (self-hosted) ou Azure AD B2C
- Usar OAuth2 Authorization Code Flow com PKCE
- Refresh tokens com rotação

## 3. Autorização

- **Modelo**: Claims-based. Acesso autorizado para qualquer token JWT válido (autenticação é suficiente). Autorização por role (`merchant`) é planejada para fase 2.
- **Isolamento de dados**: O `merchantId` é extraído do JWT (`sub` claim). Cada comerciante acessa **apenas seus próprios dados**. Nenhum endpoint aceita `merchantId` como parâmetro de query/body — é sempre extraído do token.
- **Princípio do menor privilégio**: A aplicação não executa queries com usuário `postgres`. Usa um usuário dedicado com permissões mínimas (SELECT, INSERT, UPDATE no schema necessário).

## 4. Proteção de APIs

### Rate Limiting (Gateway)

- **Algoritmo**: Token Bucket
- **Parâmetros**: 50 tokens/segundo (estado estável), burst de 100, fila de 10
- **Comportamento sob sobrecarga**: Requests além da fila retornam HTTP 429 com header `Retry-After: 1`
- **Garantia**: Máximo 5% de perda (fila de 10 absorve picos curtos)

### Headers de Segurança

O Gateway adiciona automaticamente:
```
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
Referrer-Policy: strict-origin-when-cross-origin
```

### CORS

- Origens permitidas configuradas explicitamente (`Cors:AllowedOrigins`)
- Wildcard `*` proibido em produção

### Input Validation

- **FluentValidation** em todos os Commands:
  - Amount: decimal > 0
  - Currency: exatamente 3 caracteres (ISO 4217)
  - Description: não vazia, máximo 500 chars
  - EntryDate: não futura
  - MerchantId: não vazio (embora extraído do token na API, validado no command)

## 5. Proteção de Dados Sensíveis

### Em Trânsito

- **Produção**: TLS 1.2+ obrigatório em todas as comunicações externas
- **Interno**: HTTP entre containers na rede Docker privada (aceito para MVP; produção: mTLS ou service mesh)

### Em Repouso

- PostgreSQL: criptografia de disco recomendada no host (AWS RDS encrypted, ou LUKS)
- Redis: não armazena dados financeiros sensíveis além de saldos agregados
- Senhas e secrets: **nunca no código de produção**. Os `appsettings.json` contêm valores de placeholder para dev. Em produção usar variáveis de ambiente ou Secret Manager.

### Logs

- **Nunca logar** tokens JWT, senhas, números de cartão ou PII
- Serilog configurado com `destructuring` para excluir campos sensíveis
- Correlation ID propagado pelo Gateway em todos os logs via header `X-Correlation-ID`. Os serviços internos também propagam o mesmo header nas suas próprias respostas.

## 6. Comunicação Inter-Serviço

- **Gateway → Services**: HTTP interno na rede Docker (`cashflow_default`). Em produção, serviços não devem expor portas diretamente (o docker-compose atual expõe para facilitar debugging — remover as seções `ports` dos serviços em produção).
- **Entries → RabbitMQ**: AMQP com autenticação (usuário dedicado `cashflow`, sem permissão de admin)
- **Consolidated → Redis**: Redis configurado com senha (`requirepass`)
- **Produção recomendada**: mTLS entre todos os serviços internos

## 7. Estratégias contra Ataques Comuns (OWASP Top 10)

| Categoria OWASP | Mitigação |
|---|---|
| A01 Broken Access Control | MerchantId extraído do JWT, nunca do body. Cada query filtra por merchantId. |
| A02 Cryptographic Failures | TLS em trânsito. Senha em variável de ambiente. JWT com HMAC-SHA256. |
| A03 Injection | EF Core com parâmetros (sem SQL raw). FluentValidation nos inputs. |
| A04 Insecure Design | Clean Architecture. Validação em camada de Application antes de tocar domínio. |
| A05 Security Misconfiguration | Swagger desabilitado em produção. Headers de segurança no gateway. CORS restrito. |
| A06 Vulnerable Components | NuGet packages com versões específicas. Dependabot recomendado. |
| A07 Auth Failures | JWT com expiração. ClockSkew zero. Validação de issuer, audience e assinatura. |
| A08 Integrity Failures | Imagens Docker com digest fixo em produção. HTTPS para pull de dependências. |
| A09 Logging Failures | Serilog estruturado. Correlation ID. Nunca logar secrets. Seq centralizado. |
| A10 SSRF | YARP com destinos fixos em configuração. Sem redirecionamentos baseados em input. |

## 8. Decisões Justificadas

1. **JWT vs Session**: JWT escolhido por escalabilidade stateless. Aceitamos que revogação imediata não é possível sem blacklist, mas expiração de 8h limita janela de risco.

2. **Rate Limiting no Gateway vs Serviços**: Centralizado no gateway para evitar duplicação e garantir que mesmo chamadas inter-serviço sejam contadas. Serviços internos têm seus próprios health checks mas não rate limiting adicional.

3. **Validação no Application Layer vs Controller**: Optamos por FluentValidation no Command/Query para que a validação seja testável independentemente do HTTP stack. O controller apenas repassa o erro de validação.
