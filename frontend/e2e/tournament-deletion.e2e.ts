import { expect, test } from '@playwright/test';
import { registerOrganizer, xsrf } from './auth';

test('cancela, mantém o contexto após falha e exclui somente o campeonato confirmado', async ({
  page,
  request,
}) => {
  const token = await registerOrganizer(request, page);
  const api = 'http://localhost:5094/api/tournaments';
  const stamp = Date.now();
  const create = async (name: string) => {
    const response = await request.post(api, {
      headers: xsrf(token),
      data: { name, sport: 'Futsal', maxTeams: 2, format: 'RoundRobin' },
    });
    expect(response.status()).toBe(201);
    return response.json();
  };
  const tournament = await create(`Excluir ${stamp}`);
  const preserved = await create(`Preservar ${stamp}`);
  const path = `/campeonatos/${tournament.id}`;
  await page.goto(path);
  await page.getByText('Configurações e acessos', { exact: true }).click();
  const trigger = page.getByRole('button', { name: 'Excluir campeonato', exact: true });
  const dialog = page.getByRole('alertdialog', { name: 'Excluir campeonato?' });
  await trigger.click();
  await dialog.getByRole('button', { name: 'Cancelar' }).click();
  await expect(dialog).toHaveCount(0);
  expect((await request.get(`${api}/${tournament.id}`)).status()).toBe(200);

  const endpoint = `**/api/tournaments/${tournament.id}`;
  await page.route(endpoint, (route) =>
    route.fulfill({ status: 409, json: { detail: 'Não foi possível concluir a exclusão.' } }),
  );
  await trigger.click();
  await dialog.getByRole('button', { name: 'Excluir campeonato' }).click();
  await expect(dialog.getByRole('alert')).toContainText('Não foi possível concluir a exclusão.');
  await expect(page).toHaveURL(new RegExp(`${path}$`));

  await page.unroute(endpoint);
  await dialog.getByRole('button', { name: 'Excluir campeonato' }).click();
  await expect(page).toHaveURL(/\/campeonatos$/);
  await expect(page.getByRole('status').filter({ hasText: 'Campeonato excluído.' })).toBeVisible();
  await expect(page.getByRole('link', { name: preserved.name, exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: tournament.name, exact: true })).toHaveCount(0);
  expect((await request.get(`${api}/${tournament.id}`)).status()).toBe(404);
});
