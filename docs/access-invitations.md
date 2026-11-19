# Convites e gestão de acesso

O Brakvyo mantém `TournamentOrganizer` e `TeamManager` como vínculos contextuais. Um
organizador administra apenas os campeonatos aos quais está vinculado; um manager
administra apenas suas equipes. `PlatformAdmin` pode administrar ambos, sem transformar
esses vínculos em roles globais.

## Criação e aceite

Um membro autorizado informa o email do convidado e recebe um caminho relativo para
`/convites/aceitar`. O link é compartilhado manualmente; o sistema não envia email,
WhatsApp ou outra notificação.

O token é protegido pelo ASP.NET Core Data Protection com o purpose versionado
`Brakvyo.AccessInvitation.v1` e validade de 72 horas. Somente o token protegido é
entregue ao cliente; ele não é armazenado no banco. As mesmas chaves persistidas já
usadas pelo ambiente Docker permitem validar o token entre reinicializações.

Para aceitar, o usuário precisa estar autenticado com o mesmo email normalizado do
convite. O backend valida o token, o estado persistido e a expiração, cria o vínculo e
marca o convite como aceito na mesma transação. Constraints únicas dos vínculos e dos
convites ativos impedem aceite ou criação duplicada.

## Revogação e remoção

Convites pendentes podem ser revogados por quem administra o recurso. Convites aceitos,
revogados ou expirados não podem ser reutilizados. Um membro não pode remover a si
próprio, nem é permitido remover o último organizer ou manager.

## Limitações atuais

- O link é copiado e compartilhado manualmente.
- Não há transferência formal de ownership.
- Não há confirmação de email, MFA ou login social neste milestone.
- O convite concede somente o vínculo contextual indicado no link.
