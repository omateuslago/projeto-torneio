# Checklist de demonstração

## Preparação

```bash
git pull
cp .env.example .env
# Defina POSTGRES_PASSWORD no .env.
bash iniciar.sh
```

Confirme antes da apresentação:

- Angular em `http://localhost:4200`;
- API em `http://localhost:5094`;
- scheduler em `http://localhost:8000/health`;
- conta de demonstração criada e sem dados pessoais reais.

## Roteiro principal

1. Entre no Brakvyo e mostre o contexto da conta.
2. Crie duas ou mais equipes e abra a gestão de managers.
3. Crie um campeonato de pontos corridos, abra e encerre as inscrições.
4. Gere os confrontos e abra a Agenda.
5. Cadastre local, quadra, disponibilidade e, se desejar, árbitro.
6. Gere um preview no Autopilot; explique Fairness e Resilience antes de aplicar.
7. Simule um imprevisto no Self-Healing, revise as mudanças e aplique o reparo.
8. Envie placares pelas duas equipes; mostre confirmação bilateral e classificação.
9. Demonstre uma divergência, resolução da contestação e atualização do resultado.
10. Abra a Central de Notificações e as preferências da conta.

## Mata-mata

1. Crie um campeonato **Mata-mata simples** com quatro equipes.
2. Encerre as inscrições e gere a chave.
3. Finalize as semifinais, confirme o avanço automático e conclua a final.
4. Mostre o campeão, o status finalizado e a rolagem interna da chave no mobile.

## Demo WhatsApp opcional

- Use uma instância Evolution e um número exclusivos de teste.
- Configure as variáveis `NOTIFICATIONS_WHATSAPP_*` somente no `.env` local.
- Habilite o número em **Preferências de notificações** e reagende uma partida.
- Confirme separadamente a delivery no Brakvyo e o recebimento no celular.
- Não grave nem compartilhe API key, token, número, segredo, QR code ou sessão.

A suíte automatizada usa provider HTTP falso; recebimento em WhatsApp real é uma
validação manual separada.

Ao encerrar a apresentação, `Ctrl+C` fecha o Angular. Os containers e o banco continuam
ativos; não use `docker compose down -v` se quiser preservar os dados.
