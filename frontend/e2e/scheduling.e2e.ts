import { expect, test } from '@playwright/test';
import { registerOrganizer, xsrf } from './auth';

test.use({ timezoneId: 'America/Sao_Paulo' });
test('configura recursos e disponibilidade, rejeita conflito e reagenda com persistência', async ({
  page,
  request,
}, testInfo) => {
  const api = 'http://localhost:5094/api';
  const token = await registerOrganizer(request, page);
  const tResponse = await request.post(`${api}/tournaments`, {
    headers: xsrf(token),
    data: { name: `Agenda ${Date.now()}`, sport: 'Futsal', maxTeams: 4, format: 'RoundRobin' },
  });
  expect(tResponse.status()).toBe(201);
  const t = await tResponse.json();
  expect(
    (
      await request.put(`${api}/tournaments/${t.id}/status`, {
        headers: xsrf(token),
        data: { status: 'Open' },
      })
    ).ok(),
  ).toBe(true);
  for (let i = 1; i <= 4; i++) {
    const response = await request.post(`${api}/teams`, {
      headers: xsrf(token),
      data: { name: `Time agenda ${i}` },
    });
    expect(response.ok()).toBe(true);
    const team = await response.json();
    expect(
      (
        await request.post(`${api}/tournaments/${t.id}/registrations`, {
          headers: xsrf(token),
          data: { teamId: team.id },
        })
      ).ok(),
    ).toBe(true);
  }
  expect(
    (
      await request.put(`${api}/tournaments/${t.id}/status`, {
        headers: xsrf(token),
        data: { status: 'Closed' },
      })
    ).ok(),
  ).toBe(true);
  expect(
    (
      await request.post(`${api}/tournaments/${t.id}/matches/generate`, { headers: xsrf(token) })
    ).ok(),
  ).toBe(true);
  await page.goto(`/campeonatos/${t.id}`);
  await page.getByRole('link', { name: 'Agenda e disponibilidade', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Partidas aguardando agendamento (6)' }),
  ).toBeVisible();
  await page.getByText('Configuração e recursos', { exact: true }).click();
  await page.getByLabel('Fuso da agenda (IANA)').fill('America/Sao_Paulo');
  await page.getByRole('button', { name: 'Salvar configuração' }).click();
  await expect(page.getByRole('button', { name: 'Salvar configuração' })).toBeEnabled();
  await page.getByLabel('Nome do local', { exact: true }).fill('Ginásio Central');
  await page.getByLabel('Endereço', { exact: true }).fill('Rua Central, 10');
  await page.getByRole('button', { name: 'Criar local', exact: true }).click();
  await page.getByLabel('Local da área').selectOption({ label: 'Ginásio Central' });
  await page.getByLabel('Nome da área').fill('Quadra 1');
  await page.getByRole('button', { name: 'Criar área', exact: true }).click();
  await page.getByLabel('Nome do árbitro').fill('Carlos');
  await page.getByRole('button', { name: 'Cadastrar árbitro' }).click();
  await expect(page.getByLabel('Nome do árbitro')).toHaveValue('');
  for (const [kind, names] of [
    ['teams', ['Time agenda 1', 'Time agenda 2', 'Time agenda 3', 'Time agenda 4']],
    ['playing-areas', ['Ginásio Central / Quadra 1']],
    ['referees', ['Carlos']],
  ] as const) {
    await page.getByLabel('Tipo de recurso').selectOption(kind);
    for (const name of names) {
      await page
        .getByRole('combobox', { name: 'Recurso', exact: true })
        .selectOption({ label: name });
      await page.getByLabel('Disponível desde').fill('2026-11-14T08:00');
      await page.getByLabel('Disponível até').fill('2026-11-14T22:00');
      await page.getByRole('button', { name: 'Adicionar período', exact: true }).click();
      await expect(page.getByRole('button', { name: 'Editar período', exact: true })).toHaveCount(
        1,
      );
    }
  }
  async function schedule(time: string) {
    await page.getByLabel('Início da partida').fill(`2026-11-14T${time}`);
    await page.getByLabel('Área da partida').selectOption({ label: 'Quadra 1' });
    await page.getByLabel('Árbitro da partida').selectOption({ label: 'Carlos' });
    await page.getByRole('button', { name: 'Salvar agendamento', exact: true }).click();
  }
  await page.getByRole('button', { name: 'Agendar partida', exact: true }).first().click();
  await schedule('18:00');
  await expect(
    page.getByRole('heading', { name: 'Partidas aguardando agendamento (5)' }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Agendar partida', exact: true }).first().click();
  await schedule('18:30');
  await expect(page.getByRole('alert')).toContainText('já possui partida');
  await expect(page.getByLabel('Início da partida')).toHaveValue('2026-11-14T18:30');
  await schedule('19:00');
  await expect(
    page.getByRole('heading', { name: 'Partidas aguardando agendamento (4)' }),
  ).toBeVisible();
  await page.getByRole('button', { name: 'Reagendar partida', exact: true }).last().click();
  await schedule('20:00');
  await expect(page.getByRole('form', { name: 'Agendamento da partida' })).toHaveCount(0);
  await page.reload();
  await expect(
    page.getByRole('heading', { name: 'Partidas aguardando agendamento (4)' }),
  ).toBeVisible();
  await expect(
    page.getByText('14/11/2026, 20:00 → 14/11/2026, 21:00', { exact: true }),
  ).toBeVisible();
  const agenda = await (await request.get(`${api}/tournaments/${t.id}/schedule`)).json();
  expect(
    agenda
      .filter((m: { startsAt: string | null }) => m.startsAt)
      .map((m: { startsAt: string }) => m.startsAt),
  ).toEqual(['2026-11-14T21:00:00Z', '2026-11-14T23:00:00Z']);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({ path: testInfo.outputPath('scheduling-mobile.png'), fullPage: true });
});
