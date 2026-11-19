# Notification Center e WhatsApp com Evolution API

## Arquitetura

Eventos importantes criam uma `Notification` e uma `NotificationDelivery` na mesma
transação da alteração de negócio. Um worker processa a outbox depois do commit e chama
somente a abstração `IWhatsAppSender`. A implementação `EvolutionWhatsAppSender` contém
todo o conhecimento específico do provider; domínio, controllers e telas não dependem
da Evolution.

O worker reivindica deliveries com `FOR UPDATE SKIP LOCKED`, libera a transação antes do
HTTP e registra sucesso, falha ou nova tentativa. Falhas transitórias usam intervalos de
1 e 5 minutos, com no máximo três tentativas. Respostas 4xx de configuração ou validação
são permanentes. `ProviderMessageId` é tratado como texto opaco.

## Desenvolvimento sem WhatsApp

O padrão é:

```env
NOTIFICATIONS_WHATSAPP_ENABLED=false
```

Nesse modo, `bash iniciar.sh` continua iniciando PostgreSQL, scheduler, migrations, API e
Angular sem Evolution, chip ou QR code. Notificações continuam persistidas e deliveries
ficam como `Skipped` (`Não habilitado` na interface).

## Configuração da Evolution API

Configure uma instalação Evolution v2 separada do Brakvyo e preencha:

```env
NOTIFICATIONS_WHATSAPP_ENABLED=true
NOTIFICATIONS_WHATSAPP_BASE_URL=https://evolution.exemplo.local
NOTIFICATIONS_WHATSAPP_API_KEY=troque-por-uma-chave-real
NOTIFICATIONS_WHATSAPP_INSTANCE_NAME=brakvyo-test
NOTIFICATIONS_WHATSAPP_WEBHOOK_SECRET=troque-por-um-segredo-longo
```

O sender usa o contrato REST v2 `POST /message/sendText/{instance}`, autenticação pelo
header `apikey` e payload `{ "number": "5511...", "text": "..." }`. Confirme esse
contrato ao atualizar a versão da Evolution, pois a API é versionada independentemente
do Brakvyo. Segredos ficam somente no ambiente e nunca devem ser commitados.

A Evolution deve usar armazenamento próprio. Não aponte sua configuração de banco para
o database/schema do Brakvyo. O `compose.yaml` principal não inclui a Evolution para não
tornar o desenvolvimento comum dependente dela; siga a documentação oficial da versão
escolhida para subir seus containers e dependências.

## Instância e QR code

1. Suba a Evolution separadamente.
2. Crie a instância indicada em `NOTIFICATIONS_WHATSAPP_INSTANCE_NAME`.
3. Configure o webhook para `/api/webhooks/evolution` e envie o segredo no header
   `X-Brakvyo-Webhook-Secret`.
4. Use a API/UI da Evolution para obter o QR code.
5. Escaneie com um número exclusivo de testes.
6. Habilite o telefone em **Preferências de notificações** no Brakvyo.

O Brakvyo não administra QR code ou sessão WhatsApp. O webhook deste milestone apenas
valida o segredo e aceita callbacks; não interpreta mensagens nem comandos inbound.

## Eventos e destinatários

- Partida agendada ou reagendada: managers das duas equipes.
- Contestação de placar aberta: organizers do campeonato.
- Contestação resolvida: managers das duas equipes.
- Reparo aplicado: somente managers das equipes presentes nas partidas alteradas.

Um usuário que gerencia as duas equipes recebe somente uma delivery por evento. A chave
de deduplicação impede recriar a mesma notificação.

## Privacidade e segurança

Cada usuário consulta e altera apenas as próprias preferências e deliveries. Telefones
de outros usuários não são expostos. API key, segredo, payload completo e telefone não
são registrados. O webhook fica indisponível sem segredo configurado e usa comparação
em tempo constante quando habilitado.

## Aviso e limitações

Evolution/Baileys é uma integração não oficial baseada no WhatsApp Web. Não use número
pessoal; prefira um número exclusivo de testes, sujeito às regras e riscos de bloqueio do
WhatsApp. Para produção real, considere migrar `IWhatsAppSender` para a Meta Cloud API
oficial.

Não há chatbot, comandos recebidos, confirmação de placar, status Delivered/Read,
mensagens em massa, mídia ou gerenciamento de QR code neste milestone.
