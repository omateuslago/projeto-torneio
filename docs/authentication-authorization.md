# Autenticação e autorização

O Brakvyo usa ASP.NET Core Identity com EF Core e PostgreSQL. `ApplicationUser`
tem identificador `Guid`, email e `DisplayName`; senha, security stamp e demais
campos sensíveis ficam sob responsabilidade do Identity.

## Sessão e CSRF

O navegador autentica por cookie `Brakvyo.Auth`, `HttpOnly`, `SameSite=Lax`, com
expiração deslizante de oito horas e opção `rememberMe`. Em produção, HTTPS faz o
cookie ser enviado como `Secure`. O frontend não armazena token em Web Storage.

Chamadas mutáveis também exigem antiforgery. O Angular obtém/renova o cookie
`XSRF-TOKEN` em `GET /api/auth/csrf` e envia seu valor no header
`X-XSRF-TOKEN`. O token é renovado depois de login e cadastro porque a identidade
da sessão mudou.

## Endpoints

- `POST /api/auth/register`: cria e autentica um usuário comum.
- `POST /api/auth/login`: inicia a sessão; falhas usam mensagem genérica.
- `POST /api/auth/logout`: encerra a sessão atual.
- `GET /api/auth/me`: retorna apenas a identidade pública e os vínculos contextuais.
- `GET /api/auth/csrf`: prepara o token antiforgery do navegador.

## Modelo de acesso

`TournamentOrganizer` relaciona usuário e campeonato. `TeamManager` relaciona
usuário e equipe. Ambos usam chaves compostas, portanto não são roles globais.
`PlatformAdmin` é a única role global e ignora verificações contextuais para
administrar dados antigos que ainda não tenham vínculos.

| Ação | Visitante | Organizer | Team Manager | PlatformAdmin |
| --- | --- | --- | --- | --- |
| Consultar dados públicos | Sim | Sim | Sim | Sim |
| Administrar campeonato, agenda, Autopilot e reparo | Não | Campeonato vinculado | Não | Sim |
| Resolver disputa | Não | Campeonato vinculado | Não | Sim |
| Enviar placar bilateral | Não | Somente se também gerenciar a equipe | Equipe vinculada e participante | Sim |
| Editar disponibilidade de equipe | Não | Campeonato vinculado | Equipe vinculada e inscrita | Sim |
| Editar disponibilidade de área/árbitro | Não | Campeonato vinculado | Não | Sim |

O backend aplica essas regras por `IAccessControl`; esconder controles no Angular
serve apenas para a experiência de uso. Uma sessão ausente recebe `401` e um
usuário autenticado sem vínculo recebe `403`.

## Bootstrap local de administrador

O bootstrap é opcional e idempotente. Defina as três variáveis antes de iniciar a
API; sem elas nenhum administrador é criado:

```bash
export Auth__BootstrapAdminEmail='admin@example.com'
export Auth__BootstrapAdminPassword='defina-uma-senha-local-forte'
export Auth__BootstrapAdminDisplayName='Admin local'
```

Não armazene a senha em arquivos versionados. Dados existentes são preservados
pela migration `AddIdentityAccessFoundation`.

## Limitações do M10

Este milestone não inclui confirmação ou recuperação de email, OAuth/OIDC, MFA,
organizações, convites, transferência de ownership ou telas para gerenciar vários
organizers/managers.
