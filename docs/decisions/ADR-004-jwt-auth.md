# ADR-004: Autenticação Stateless com JWT Bearer

**Status:** Aceito
**Data:** 2024-01-01  
**Atualizado:** 2026-07-17 — Validação de credenciais (PBKDF2), policy `merchant-only`, JWT enforced no edge
**Decisores:** Time de Arquitetura
**Depende de:** [ADR-001](ADR-001-microservices.md)

---

## Contexto

Os serviços precisam autenticar requisições de comerciantes e garantir que cada comerciante acesse **apenas seus próprios dados**. A solução deve:

- Funcionar de forma stateless (sem sessão centralizada) para suportar escala horizontal
- Isolar dados por `merchantId` sem exigir que o cliente informe esse ID (seria falseado facilmente)
- Ser compatível com o padrão de API Gateway: o Gateway valida o token e os serviços internos re-validam (defense in depth)

## Drivers de Decisão

- **Seguridade**: `merchantId` deve ser extraído do token assinado, nunca do body ou query string
- **Escalabilidade**: Sem servidor de sessão compartilhado
- **Simplicidade**: Para o MVP, evitar dependência de IdP externo
- **Defense in depth**: Validação em duas camadas (Gateway + serviço interno)

## Decisão

Usar **JWT (JSON Web Tokens)** com assinatura **HMAC-SHA256** (algoritmo `HS256`). O Gateway emite e valida tokens. Os serviços internos também validam o JWT de forma independente.

### Estrutura do Token

```json
{
  "sub": "<merchantId — UUID estável por usuário>",
  "jti": "<UUID único por token>",
  "iat": "<timestamp de emissão>",
  "exp": "<iat + 8 horas>",
  "iss": "cashflow-gateway",
  "aud": "cashflow-services"
}
```

> O claim `sub` é um UUID estável derivado do username via hash SHA-256 (determinístico). Isso garante que o mesmo usuário sempre recebe o mesmo `merchantId`, mesmo em instâncias diferentes do Gateway sem estado compartilhado.

### Parâmetros de Validação

| Parâmetro | Valor | Justificativa |
|---|---|---|
| Algoritmo | HMAC-SHA256 | Simples, seguro, sem infra de PKI |
| Chave | ≥ 32 bytes via `JWT_SECRET_KEY` (env var) | Nunca em código ou repositório |
| Expiração | 8 horas | Equilíbrio entre usabilidade e janela de risco |
| ClockSkew | Zero | Sem tolerância — evita abuso de tokens quase-expirados |
| Validações ativas | Issuer, Audience, Lifetime, Signature | Todas habilitadas |

### Fluxo de Autenticação

```
1. Comerciante  →  POST /api/auth/token {username, password}
2. Gateway      →  Valida credenciais no DemoUserStore via PBKDF2-SHA256
                   (produção: substituir por IdP com OAuth2)
3. Gateway      →  Emite JWT assinado (sub = DeterministicGuid(username))
4. Comerciante  →  Authorization: Bearer <token> em todas as requisições
5. Gateway      →  Valida JWT E policy merchant-only antes de proxiar
6. Serviço      →  Re-valida JWT + policy merchant-only (defense in depth)
7. Serviço      →  Extrai merchantId do claim 'sub' — isolamento de dados
```

## Consequências

**Positivo:**
- [OK] Stateless: qualquer instância do Gateway valida qualquer token sem estado compartilhado
- [OK] `merchantId` vem do token assinado — impossível de forjar sem a chave
- [OK] Defense in depth: comprometimento do Gateway não bypassa validação nos serviços
- [OK] Zero dependência externa no MVP
- [OK] Policy `merchant-only` verifica claim `role = "merchant"` em todos os endpoints protegidos
- [OK] Gateway rejeita requisições sem JWT válido antes de fazer proxy (edge enforcement)
- [OK] Credenciais demo validadas com PBKDF2 (100.000 iterações): qualquer senha não é mais aceita

**Negativo / Trade-offs:**
- [!] Revogação imediata não é possível sem blacklist (token válido por até 8h após comprometimento)
- [!] Chave simétrica compartilhada: se vazada, todos os tokens podem ser forjados
- [!] DemoUserStore com salt fixo: não é prod-ready (salt único por usuário exigido em produção)

## Recomendação para Produção

Substituir o endpoint de auth demo por um IdP dedicado com OAuth2 Authorization Code Flow + PKCE:

| Opção | Tipo | Recomendado quando |
|---|---|---|
| **Keycloak** | Self-hosted, open source | Controle total, on-premise |
| **Azure AD B2C** | Managed, cloud | Azure como cloud principal |
| **Auth0** | SaaS | Velocidade de entrega > controle |

## Alternativas Rejeitadas

| Alternativa | Motivo da Rejeição |
|---|---|
| API Key estática | Sem expiração nativa, sem `merchantId` no token, difícil rotacionar |
| Session + Cookie | Requer armazenamento centralizado de sessão — quebra escala horizontal |
| mTLS | Adequado para comunicação inter-serviço, não para usuário final |
| OAuth2 completo (MVP) | Complexidade não justificada para o MVP; recomendado apenas em produção |

## Referências

- [Security Design Document](../security/security-design.md)
- [ADR-001: Arquitetura de Microservices](ADR-001-microservices.md)
