import { expect, test } from '@playwright/test';
import { registerOrganizer, xsrf } from './auth';

test('cria equipes e campeonato, persiste após recarregar e gerencia inscrições', async ({
  page,
  request,
}, testInfo) => {
  await registerOrganizer(request, page);
  const suffix = Date.now();
  const names = [`Azul ${suffix}`, `Verde ${suffix}`, `Laranja ${suffix}`];
  await page.goto('/equipes');
  for (const name of names) {
    await page.getByRole('button', { name: '+ Criar equipe', exact: true }).click();
    await page.getByLabel('Nome da equipe').fill(name);
    await page.getByRole('button', { name: 'Criar equipe', exact: true }).click();
    await expect(page.getByText(`Equipe ${name} cadastrada.`, { exact: true })).toBeVisible();
    await expect(page.locator('.team-card').filter({ hasText: name })).toBeVisible();
  }
  const teamCard = page.locator('.team-card').filter({ hasText: names[0] });
  await teamCard.getByRole('button', { name: 'Gerenciar acesso', exact: true }).click();
  await expect(teamCard.getByText('Managers', { exact: true })).toBeVisible();
  await teamCard.getByRole('button', { name: 'Fechar gestão de acesso' }).click();
  await expect(teamCard.getByText('Managers', { exact: true })).toHaveCount(0);
  await expect(teamCard.getByRole('heading', { name: names[0] })).toBeVisible();
  await page.getByRole('link', { name: 'Campeonatos', exact: true }).click();
  await page.getByRole('button', { name: '+ Criar', exact: true }).click();
  await page.getByLabel('Nome do campeonato').fill(`Copa ${suffix}`);
  await page.getByLabel('Esporte').selectOption('Futsal');
  await page.getByLabel('Número de equipes').fill('2');
  await page.getByRole('button', { name: 'Criar campeonato', exact: true }).click();
  await page.getByRole('link', { name: `Copa ${suffix}`, exact: true }).click();
  await expect(page.getByRole('heading', { name: `Copa ${suffix}`, exact: true })).toBeVisible();
  await expect(page).toHaveURL(/\/campeonatos\/[0-9a-f-]+$/);
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
  await page.getByRole('button', { name: 'Encerrar inscrições' }).click();
  await page
    .getByRole('alertdialog')
    .getByRole('button', { name: 'Encerrar inscrições', exact: true })
    .click();
  await expect(page.getByText('Inscrições encerradas.', { exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByText('2 de 2 vagas preenchidas')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Inscrever equipe', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Gerar confrontos', exact: true }).click();
  await expect(
    page.getByRole('list', { name: 'Partidas do campeonato' }).getByRole('listitem'),
  ).toHaveCount(1);
  await expect(page.locator('.badge')).toHaveText('Em andamento');
  await page.getByText('Mais ações', { exact: true }).click();
  await page
    .getByRole('button', { name: 'Registrar resultado administrativamente', exact: true })
    .click();
  let resultForm = page.getByRole('form', { name: 'Resultado da partida' });
  await resultForm.locator('input[formcontrolname="homeScore"]').fill('-1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Informe placares inteiros');
  await resultForm.locator('input[formcontrolname="homeScore"]').fill('3');
  await resultForm.locator('input[formcontrolname="awayScore"]').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(page.locator('.badge')).toHaveText('Finalizado');
  const standings = page.getByRole('table', { name: 'Classificação do campeonato' });
  await expect(standings.getByRole('row').nth(1).getByRole('cell').nth(2)).toHaveText('3');
  await page.getByRole('link', { name: 'Ver classificação final' }).click();
  await expect(page).toHaveURL(detailUrl + '#classificacao');
  await expect(page.getByRole('heading', { name: 'Classificação final' })).toBeInViewport();
  await page.reload();
  await expect(standings.getByRole('row').filter({ hasText: names[0] })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Classificação final' })).toBeInViewport();
  await page.goBack();
  await expect(page).toHaveURL(detailUrl);
  await page.goForward();
  await expect(page).toHaveURL(detailUrl + '#classificacao');
  await expect(page.getByRole('heading', { name: 'Classificação final' })).toBeInViewport();
  await page.goBack();
  await expect(page).toHaveURL(detailUrl);
  await page.getByText('Mais ações', { exact: true }).click();
  await page
    .getByRole('button', { name: 'Corrigir resultado administrativamente', exact: true })
    .click();
  resultForm = page.getByRole('form', { name: 'Resultado da partida' });
  await resultForm.locator('input[formcontrolname="homeScore"]').fill('1');
  await resultForm.locator('input[formcontrolname="awayScore"]').fill('1');
  await page.getByRole('button', { name: 'Salvar resultado', exact: true }).click();
  await expect(standings.getByRole('row').nth(1).getByRole('cell').nth(2)).toHaveText('1');
  await expect(standings.getByRole('row').nth(2).getByRole('cell').nth(2)).toHaveText('1');
  await page.reload();
  await expect(page.locator('.badge')).toHaveText('Finalizado');
  await expect(standings.getByRole('row').nth(1).getByRole('cell').nth(2)).toHaveText('1');
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
  const token = await registerOrganizer(request, page);
  const created = await request.post('http://localhost:5094/api/tournaments', {
    headers: xsrf(token),
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
    headers: xsrf(token),
    data: { name: `Equipe concorrente ${Date.now()}` },
  });
  const team = await createdTeam.json();
  await request.put(`http://localhost:5094/api/tournaments/${tournament.id}/status`, {
    headers: xsrf(token),
    data: { status: 'Open' },
  });
  await page.goto(`/campeonatos/${tournament.id}`);
  await page.getByRole('combobox', { name: 'Equipe', exact: true }).selectOption(team.id);
  await request.put(`http://localhost:5094/api/tournaments/${tournament.id}/status`, {
    headers: xsrf(token),
    data: { status: 'Closed' },
  });
  await page.getByRole('button', { name: 'Inscrever equipe', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('inscrições deste campeonato estão fechadas');
});
