# ADR-004: JWT Bearer para Autenticação Stateless

**Status:** Aceito  
**Data:** 2024-01-01

## Contexto

Os serviços precisam autenticar requisições de comerciantes. A solução deve ser escalável horizontalmente (sem sessão centralizada) e compatível com o padrão de microservices.

## Decisão

Usar **JWT (JSON Web Tokens)** com assinatura **HMAC-SHA256**. O Gateway emite e valida tokens. Os serviços internos também validam o JWT (defense in depth). No MVP, o próprio Gateway expõe um endpoint `/api/auth/token` para emissão (demo). Em produção, substituir por Keycloak ou Azure AD B2C.

## Claims no Token

```json
{
  "sub": "<merchantId (UUID)>",
  "jti": "<token único>",
  "iat": "<timestamp>",
  "exp": "<+8h>",
  "iss": "cashflow-gateway",
  "aud": "cashflow-services",
  "role": "merchant"
}
```

## Configuração de Segurança

- **Algoritmo**: HMAC-SHA256 (simétrico, chave ≥32 bytes via variável de ambiente)
- **Expiração**: 8 horas
- **ClockSkew**: Zero (sem tolerância)
- **Validações**: Issuer, Audience, Lifetime, Signature

## Trade-offs

| Aspecto | JWT | Session/Cookie |
|---|---|---|
| Escalabilidade | **Stateless — sem servidor de sessão** | Requer sticky sessions ou Redis session |
| Revogação | Difícil (TTL fixo) | Imediata |
| Overhead de rede | Token viaja em cada request | Apenas session ID |
| Complexidade | Baixa | Baixa |

## Produção: Recomendação

Para produção, substituir o endpoint de auth demo por:
1. **Keycloak** (self-hosted, open source)
2. **Azure AD B2C** (managed, cloud)
3. **Auth0** (SaaS)

## Alternativas Rejeitadas

- **API Key**: Sem expiração nativa, sem informações de identidade no token.
- **OAuth2 com Authorization Code Flow**: Ideal para produção com usuários reais. Complexidade justificada apenas em produção.
- **mTLS**: Adequado para comunicação inter-serviço, não para usuário final.
