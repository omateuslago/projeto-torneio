import { APIRequestContext, expect, Page, test } from '@playwright/test';
import { registerOrganizer, xsrf } from './auth';

const api = 'http://localhost:5094/api';

async function createBracket(request: APIRequestContext, token: string, teamCount: number) {
  const stamp = `${Date.now()}-${teamCount}-${Math.random().toString(16).slice(2)}`;
  const created = await request.post(`${api}/tournaments`, {
    headers: xsrf(token),
    data: {
      name: `Chave ${teamCount} ${stamp}`,
      sport: 'Futsal',
      maxTeams: teamCount,
      format: 'SingleElimination',
    },
  });
  expect(created.status()).toBe(201);
  const tournament = await created.json();
  await request.put(`${api}/tournaments/${tournament.id}/status`, {
    headers: xsrf(token),
    data: { status: 'Open' },
  });
  for (let seed = 1; seed <= teamCount; seed++) {
    const teamResponse = await request.post(`${api}/teams`, {
      headers: xsrf(token),
      data: { name: `Seed ${seed.toString().padStart(2, '0')} ${stamp}` },
    });
    const team = await teamResponse.json();
    await request.post(`${api}/tournaments/${tournament.id}/registrations`, {
      headers: xsrf(token),
      data: { teamId: team.id },
    });
  }
  await request.put(`${api}/tournaments/${tournament.id}/status`, {
    headers: xsrf(token),
    data: { status: 'Closed' },
  });
  const generated = await request.post(`${api}/tournaments/${tournament.id}/matches/generate`, {
    headers: xsrf(token),
  });
  expect(generated.ok()).toBe(true);
  return tournament;
}

async function recordNextPending(page: Page, home: string, away: string) {
  const card = page.locator('.match-card').filter({ hasText: 'Pendente' }).first();
  await card.getByText('Mais ações', { exact: true }).click();
  await card
    .getByRole('button', { name: 'Registrar resultado administrativamente', exact: true })
    .click();
  await page.getByLabel('Placar mandante').fill(home);
  await page.getByLabel('Placar visitante').fill(away);
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
}

test('percorre semifinais e final, rejeita empate e destaca o campeão', async ({
  page,
  request,
}) => {
  const token = await registerOrganizer(request, page);
  const stamp = `${Date.now()}-ui`;
  const teamNames: string[] = [];
  for (let seed = 1; seed <= 4; seed++) {
    const name = `Equipe mata-mata ${seed} ${stamp}`;
    teamNames.push(name);
    const response = await request.post(`${api}/teams`, {
      headers: xsrf(token),
      data: { name },
    });
    expect(response.status()).toBe(201);
  }

  await page.goto('/campeonatos');
  await page.getByRole('button', { name: '+ Criar', exact: true }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa mata-mata ${stamp}`);
  await page.getByLabel('Número de equipes').fill('4');
  await page.getByRole('radio', { name: /Mata-mata simples/ }).check();
  await page.getByRole('button', { name: 'Criar campeonato', exact: true }).click();
  await page.getByRole('link', { name: `Copa mata-mata ${stamp}`, exact: true }).click();
  await page.getByRole('button', { name: 'Abrir inscrições', exact: true }).click();
  for (const [index, name] of teamNames.entries()) {
    await page.getByRole('combobox', { name: 'Equipe', exact: true }).selectOption({ label: name });
    await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
    await expect(page.getByText(`${index + 1} de 4 vagas preenchidas`)).toBeVisible();
  }
  await page.getByRole('button', { name: 'Encerrar inscrições', exact: true }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Encerrar inscrições', exact: true })
    .click();
  await page.getByRole('button', { name: 'Gerar chave', exact: true }).first().click();

  await expect(page.getByRole('heading', { name: 'Chave eliminatória' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Semifinais' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Final', exact: true })).toBeVisible();
  await expect(page.getByRole('table', { name: 'Classificação do campeonato' })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Ver agenda' })).toHaveCount(0);

  const detailUrl = page.url();
  const matchLink = page.getByRole('link', { name: 'Gerenciar resultado' }).first();
  const matchHref = (await matchLink.getAttribute('href'))!;
  const targetCard = page.locator(new URL(matchHref, detailUrl).hash);
  await matchLink.click();
  await expect(page).toHaveURL(new URL(matchHref, detailUrl).href);
  await expect(targetCard).toBeInViewport();
  await page.goBack();
  await expect(page).toHaveURL(detailUrl);
  await page.goForward();
  await expect(targetCard).toBeInViewport();
  await page.reload();
  await expect(targetCard).toBeInViewport();

  await recordNextPending(page, '2', '0');
  await expect(page.getByText('Resultado salvo. Chave atualizada.')).toBeVisible();
  await recordNextPending(page, '1', '0');
  await expect(page.locator('.match-card').filter({ hasText: 'Pendente' })).toHaveCount(1);

  await recordNextPending(page, '1', '1');
  await expect(page.getByRole('alert')).toContainText('precisam de um vencedor');
  await page.getByLabel('Placar mandante').fill('3');
  await page.getByLabel('Placar visitante').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();

  await expect(page.locator('.badge')).toHaveText('Finalizado');
  await expect(page.getByText('Campeão', { exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Ver chave final' }).click();
  await expect(page).toHaveURL(detailUrl + '#chave');
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Chave eliminatória' })).toBeInViewport();
  await expect(page.getByText('Campeão', { exact: true })).toBeVisible();
});

test('renderiza chaves com e sem byes sem overflow da página', async ({
  page,
  request,
}, testInfo) => {
  const token = await registerOrganizer(request, page);
  for (const teamCount of [4, 5, 8, 16]) {
    const tournament = await createBracket(request, token, teamCount);
    const roundCount = Math.ceil(Math.log2(teamCount));
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/campeonatos/${tournament.id}`);
    await expect(page.locator('.bracket-round')).toHaveCount(roundCount);
    await expect(page.locator('.bracket-round').first().locator('.bracket-node')).toHaveCount(
      2 ** (roundCount - 1),
    );
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    ).toBe(true);
    expect(
      await page
        .locator('.bracket-team')
        .evaluateAll((rows) =>
          rows.every(
            (row) =>
              row.getBoundingClientRect().right <= row.parentElement!.getBoundingClientRect().right,
          ),
        ),
    ).toBe(true);
    await page.screenshot({
      path: testInfo.outputPath(`bracket-${teamCount}-desktop.png`),
      fullPage: true,
    });

    await page.setViewportSize({ width: 360, height: 800 });
    expect(
      await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
    ).toBe(true);
    expect(
      await page
        .locator('.bracket-viewport')
        .evaluate((element) => element.scrollWidth > element.clientWidth),
    ).toBe(true);
    await page.screenshot({
      path: testInfo.outputPath(`bracket-${teamCount}-mobile.png`),
      fullPage: true,
    });
  }
});
