import { expect, test } from '@playwright/test';

test('cria equipes e campeonato, persiste após recarregar e gerencia inscrições', async ({
  page,
}, testInfo) => {
  const suffix = Date.now();
  const names = [`Azul ${suffix}`, `Verde ${suffix}`, `Laranja ${suffix}`];
  await page.goto('/equipes');
  for (const name of names) {
    await page.getByLabel('Nome da equipe').fill(name);
    await page.getByRole('button', { name: 'Criar equipe', exact: true }).click();
    await expect(page.getByText(`Equipe ${name} cadastrada.`, { exact: true })).toBeVisible();
    await expect(page.getByRole('listitem').filter({ hasText: name })).toBeVisible();
  }
  await page.getByRole('link', { name: 'Campeonatos', exact: true }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa ${suffix}`);
  await page.getByLabel('Modalidade').fill('Futsal');
  await page.getByLabel('Limite de equipes').fill('2');
  await page.getByRole('button', { name: 'Criar campeonato', exact: true }).click();
  await expect(page.getByRole('heading', { name: `Copa ${suffix}`, exact: true })).toBeVisible();
  const detailUrl = page.url();
  await page.reload();
  await expect(page.getByRole('heading', { name: `Copa ${suffix}`, exact: true })).toBeVisible();
  await page.getByRole('link', { name: 'Voltar aos campeonatos' }).click();
  await page.getByRole('link', { name: `Copa ${suffix}`, exact: true }).click();
  await expect(page).toHaveURL(detailUrl);
  await page.getByRole('button', { name: 'Abrir inscrições' }).click();
  await expect(page.getByText('Inscrições abertas.', { exact: true })).toBeVisible();
  await page
    .getByRole('combobox', { name: 'Equipe', exact: true })
    .selectOption({ label: names[0] });
  await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
  await expect(page.getByText('1 de 2 vagas preenchidas')).toBeVisible();
  await page
    .getByRole('combobox', { name: 'Equipe', exact: true })
    .selectOption({ label: names[0] });
  await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('já está inscrita');
  await page
    .getByRole('combobox', { name: 'Equipe', exact: true })
    .selectOption({ label: names[1] });
  await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
  await expect(page.getByText('2 de 2 vagas preenchidas')).toBeVisible();
  await page
    .getByRole('combobox', { name: 'Equipe', exact: true })
    .selectOption({ label: names[2] });
  await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('campeonato está lotado');
  await page.getByRole('button', { name: 'Fechar inscrições' }).click();
  await expect(page.getByText('Inscrições encerradas.', { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText('2 de 2 vagas preenchidas')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Inscrever equipe', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Gerar confrontos', exact: true }).click();
  await expect(
    page.getByRole('table', { name: 'Partidas do campeonato' }).getByRole('row'),
  ).toHaveCount(2);
  await expect(page.getByText('Em andamento', { exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Registrar resultado', exact: true }).click();
  await page.getByLabel('Placar mandante').fill('-1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Informe placares inteiros');
  await page.getByLabel('Placar mandante').fill('3');
  await page.getByLabel('Placar visitante').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.getByText('Finalizado', { exact: true })).toBeVisible();
  const standings = page.getByRole('table', { name: 'Classificação do campeonato' });
  await expect(standings.getByRole('row').nth(1).getByRole('cell').nth(1)).toHaveText('3');
  await page.getByRole('button', { name: 'Corrigir resultado', exact: true }).click();
  await page.getByLabel('Placar mandante').fill('1');
  await page.getByLabel('Placar visitante').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(standings.getByRole('row').nth(1).getByRole('cell').nth(1)).toHaveText('1');
  await expect(standings.getByRole('row').nth(2).getByRole('cell').nth(1)).toHaveText('1');
  await page.reload();
  await expect(page.getByText('Finalizado', { exact: true })).toBeVisible();
  await expect(standings.getByRole('row').nth(1).getByRole('cell').nth(1)).toHaveText('1');
  await page.screenshot({ path: testInfo.outputPath('desktop.png'), fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.screenshot({ path: testInfo.outputPath('mobile.png'), fullPage: true });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
});

test('mostra erro real se as inscrições fecharem enquanto o formulário está aberto', async ({
  page,
  request,
}) => {
  const created = await request.post('http://localhost:5094/api/tournaments', {
    data: {
      name: `Inscrições concorrentes ${Date.now()}`,
      sport: 'Futsal',
      maxTeams: 2,
      format: 'RoundRobin',
    },
  });
  expect(created.status()).toBe(201);
  const tournament = await created.json();
  const createdTeam = await request.post('http://localhost:5094/api/teams', {
    data: { name: `Equipe concorrente ${Date.now()}` },
  });
  const team = await createdTeam.json();
  await request.put(`http://localhost:5094/api/tournaments/${tournament.id}/status`, {
    data: { status: 'Open' },
  });
  await page.goto(`/campeonatos/${tournament.id}`);
  await page.getByRole('combobox', { name: 'Equipe', exact: true }).selectOption(team.id);
  await request.put(`http://localhost:5094/api/tournaments/${tournament.id}/status`, {
    data: { status: 'Closed' },
  });
  await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('inscrições deste campeonato estão fechadas');
});
