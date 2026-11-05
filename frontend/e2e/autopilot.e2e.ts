import { expect, test, APIRequestContext, Page } from '@playwright/test';
test.use({ timezoneId: 'UTC' });
const api = 'http://localhost:5094/api';
async function seed(request: APIRequestContext, unavailable = false) {
  async function post(path: string, data: unknown) {
    const r = await request.post(api + path, { data });
    expect(r.ok()).toBe(true);
    return r.json();
  }
  const t = await post('/tournaments', {
    name: `Autopilot ${Date.now()}`,
    sport: 'Futsal',
    maxTeams: 4,
    format: 'RoundRobin',
  });
  expect(
    (await request.put(`${api}/tournaments/${t.id}/status`, { data: { status: 'Open' } })).ok(),
  ).toBe(true);
  const window = { startsAt: '2026-11-14T18:00:00Z', endsAt: '2026-11-15T02:00:00Z' };
  let teamId = '';
  for (let i = 1; i <= 4; i++) {
    const team = await post('/teams', { name: `Autopilot time ${i}` });
    teamId = team.id;
    await post(`/tournaments/${t.id}/registrations`, { teamId: team.id });
    if (!unavailable || i !== 1)
      await post(`/tournaments/${t.id}/teams/${team.id}/availability`, window);
  }
  expect(
    (await request.put(`${api}/tournaments/${t.id}/status`, { data: { status: 'Closed' } })).ok(),
  ).toBe(true);
  const matches = await post(`/tournaments/${t.id}/matches/generate`, {});
  const venue = await post(`/tournaments/${t.id}/venues`, {
    name: 'Arena Autopilot',
    address: 'Rua Central',
  });
  const area = await post(`/tournaments/${t.id}/venues/${venue.id}/playing-areas`, {
    name: 'Quadra Autopilot',
  });
  const referee = await post(`/tournaments/${t.id}/referees`, { name: 'Árbitro Autopilot' });
  await post(`/tournaments/${t.id}/playing-areas/${area.id}/availability`, window);
  await post(`/tournaments/${t.id}/referees/${referee.id}/availability`, window);
  return { t, matches, area, referee, teamId };
}
async function generate(page: Page, id: string) {
  await page.goto(`/campeonatos/${id}/agenda`);
  await page.getByLabel('Início do horizonte').fill('2026-11-14T18:00');
  await page.getByLabel('Fim do horizonte').fill('2026-11-15T02:00');
  await page
    .getByLabel('Árbitros no agendamento automático')
    .selectOption({ label: 'Exigir árbitro em todas as partidas' });
  await page.getByRole('button', { name: 'Gerar agenda automaticamente', exact: true }).click();
}
test('gera preview real, preserva reserva fixa e aplica todas as partidas', async ({
  page,
  request,
}, testInfo) => {
  const s = await seed(request);
  const fixed = await request.put(`${api}/matches/${s.matches[0].id}/schedule`, {
    data: { startsAt: '2026-11-14T21:00:00Z', playingAreaId: s.area.id, refereeId: s.referee.id },
  });
  expect(fixed.ok()).toBe(true);
  await generate(page, s.t.id);
  await expect(
    page.getByRole('heading', { name: 'Proposta de agenda', exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText('Encontrei uma agenda válida para todas as 5 partidas pendentes.'),
  ).toBeVisible();
  const before = await (await request.get(`${api}/tournaments/${s.t.id}/schedule`)).json();
  expect(before.filter((m: { startsAt: string | null }) => m.startsAt)).toHaveLength(1);
  await page.getByRole('button', { name: 'Aplicar agenda', exact: true }).click();
  await expect(page.getByText('Agenda aplicada.', { exact: true })).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole('heading', { name: 'Partidas aguardando agendamento (0)' }),
  ).toBeVisible();
  const after = await (await request.get(`${api}/tournaments/${s.t.id}/schedule`)).json();
  expect(after.filter((m: { startsAt: string | null }) => m.startsAt)).toHaveLength(6);
  expect(after.find((m: { matchId: string }) => m.matchId === s.matches[0].id).startsAt).toBe(
    '2026-11-14T21:00:00Z',
  );
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
  await page.screenshot({ path: testInfo.outputPath('autopilot-mobile.png'), fullPage: true });
});
test('problema impossível explica a equipe indisponível sem gravar agenda', async ({
  page,
  request,
}) => {
  const s = await seed(request, true);
  await generate(page, s.t.id);
  await expect(
    page.getByText('Não existe agenda compatível com os dados e a grade solicitados.'),
  ).toBeVisible();
  await expect(
    page.getByText('Equipe Autopilot time 1 não possui janela suficiente no horizonte.'),
  ).toBeVisible();
  await expect(page.getByRole('button', { name: 'Aplicar agenda', exact: true })).toHaveCount(0);
  const rows = await (await request.get(`${api}/tournaments/${s.t.id}/schedule`)).json();
  expect(rows.every((m: { startsAt: string | null }) => m.startsAt === null)).toBe(true);
});
test('mudança entre preview e aplicação rejeita proposta antiga', async ({ page, request }) => {
  const s = await seed(request);
  await generate(page, s.t.id);
  await expect(page.getByRole('button', { name: 'Aplicar agenda', exact: true })).toBeVisible();
  expect(
    (
      await request.post(`${api}/tournaments/${s.t.id}/teams/${s.teamId}/availability`, {
        data: { startsAt: '2026-11-16T10:00:00Z', endsAt: '2026-11-16T12:00:00Z' },
      })
    ).ok(),
  ).toBe(true);
  await page.getByRole('button', { name: 'Aplicar agenda', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('mudaram desde a geração');
  const rows = await (await request.get(`${api}/tournaments/${s.t.id}/schedule`)).json();
  expect(rows.every((m: { startsAt: string | null }) => m.startsAt === null)).toBe(true);
});
