import { expect, test } from '@playwright/test';

test('campeonato completo com envios divergentes, resolução e persistência', async ({
  page,
}, testInfo) => {
  const stamp = Date.now();
  const teams = [`Azul bilateral ${stamp}`, `Verde bilateral ${stamp}`];
  await page.goto('/equipes');
  for (const name of teams) {
    await page.getByLabel('Nome da equipe').fill(name);
    await page.getByRole('button', { name: 'Criar equipe', exact: true }).click();
    await expect(page.getByText(`Equipe ${name} cadastrada.`, { exact: true })).toBeVisible();
  }
  await page.getByRole('link', { name: 'Campeonatos', exact: true }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa bilateral ${stamp}`);
  await page.getByLabel('Modalidade').fill('Futsal');
  await page.getByLabel('Limite de equipes').fill('2');
  await page.getByRole('button', { name: 'Criar campeonato', exact: true }).click();
  await page.getByRole('button', { name: 'Abrir inscrições', exact: true }).click();
  for (const [index, name] of teams.entries()) {
    await page.getByRole('combobox', { name: 'Equipe', exact: true }).selectOption({ label: name });
    await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
    await expect(page.getByText(`${index + 1} de 2 vagas preenchidas`)).toBeVisible();
  }
  await page.getByRole('button', { name: 'Fechar inscrições', exact: true }).click();
  await page.getByRole('button', { name: 'Gerar confrontos', exact: true }).click();
  await page.getByRole('button', { name: `Enviar como ${teams[0]}`, exact: true }).click();
  await page.getByLabel('Placar mandante').fill('3');
  await page.getByLabel('Placar visitante').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pendências (1)', exact: true })).toBeVisible();
  await expect(
    page.getByText(`Aguardando resultado de ${teams[1]}.`, { exact: false }),
  ).toBeVisible();
  await page.reload();
  await page.getByRole('button', { name: `Enviar como ${teams[1]}`, exact: true }).click();
  await page.getByLabel('Placar mandante').fill('2');
  await page.getByLabel('Placar visitante').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.getByRole('table', { name: 'Partidas do campeonato' })).toContainText(
    'Resultado contestado',
  );
  const table = page.getByRole('table', { name: 'Classificação do campeonato' });
  await expect(table.getByRole('row').nth(1).getByRole('cell').nth(1)).toHaveText('0');
  await expect(page.getByText('Em andamento', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Resolver disputa', exact: true }).click();
  await page.getByLabel('Placar mandante').fill('3');
  await page.getByLabel('Placar visitante').fill('1');
  await page.getByLabel('Motivo da decisão').fill('Placar confirmado pela súmula.');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Pendências (0)', exact: true })).toBeVisible();
  await expect(page.getByText('Finalizado', { exact: true })).toBeVisible();
  await expect(table.getByRole('row').nth(1).getByRole('cell').nth(1)).toHaveText('3');
  await page.reload();
  await expect(page.getByText('Decisão: Placar confirmado pela súmula.')).toBeVisible();
  await expect(page.getByRole('table', { name: 'Partidas do campeonato' })).toContainText(
    'Disputa resolvida',
  );
  await page.screenshot({ path: testInfo.outputPath('bilateral-desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: testInfo.outputPath('bilateral-mobile.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
});

test('envios concordantes confirmam automaticamente sem intervenção', async ({ page, request }) => {
  const api = 'http://localhost:5094/api';
  const response = await request.post(`${api}/tournaments`, {
    data: {
      name: `Concordância ${Date.now()}`,
      sport: 'Futsal',
      maxTeams: 2,
      format: 'RoundRobin',
    },
  });
  const tournament = await response.json();
  expect(response.status()).toBe(201);
  await request.put(`${api}/tournaments/${tournament.id}/status`, { data: { status: 'Open' } });
  for (const name of ['Equipe A', 'Equipe B']) {
    const team = await (await request.post(`${api}/teams`, { data: { name } })).json();
    await request.post(`${api}/tournaments/${tournament.id}/registrations`, {
      data: { teamId: team.id },
    });
  }
  await request.put(`${api}/tournaments/${tournament.id}/status`, { data: { status: 'Closed' } });
  await request.post(`${api}/tournaments/${tournament.id}/matches/generate`);
  await page.goto(`/campeonatos/${tournament.id}`);
  for (const team of ['Equipe A', 'Equipe B']) {
    await page.getByRole('button', { name: `Enviar como ${team}`, exact: true }).click();
    await page.getByLabel('Placar mandante').fill('1');
    await page.getByLabel('Placar visitante').fill('1');
    await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
    await expect(page.getByRole('form', { name: 'Resultado da partida' })).toHaveCount(0);
  }
  await expect(page.getByText('Resultado confirmado pelas equipes', { exact: true })).toBeVisible();
  await expect(page.getByText('Finalizado', { exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Pendências (0)', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Resolver disputa' })).toHaveCount(0);
});
