import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Repair, RepairProposal } from './repair';
const row = { matchId: 'm', startsAt: '2026-11-14T18:00:00Z', playingAreaId: 'a', refereeId: null };
const proposal: RepairProposal = {
  status: 'optimal',
  incident: {},
  changes: [{ before: row, after: { ...row, startsAt: '2026-11-14T20:00:00Z' } }],
  metrics: {
    impactedCount: 1,
    changedCount: 1,
    preservedCount: 2,
    totalDisplacementMinutes: 120,
    maxDisplacementMinutes: 120,
    areaChanges: 0,
    refereeChanges: 0,
  },
  warnings: [],
  fairness: null,
  resilience: null,
  proposalToken: 'protected',
};
describe('Self-Healing', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Repair],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup() {
    const f = TestBed.createComponent(Repair);
    f.componentRef.setInput('tournamentId', 'cup');
    f.componentRef.setInput('matches', [
      {
        matchId: 'm',
        homeTeam: 'Azul',
        awayTeam: 'Verde',
        startsAt: row.startsAt,
        matchStatus: 'Pending',
      },
    ]);
    f.componentRef.setInput('areas', [{ id: 'a', name: 'Quadra' }]);
    f.componentRef.setInput('referees', [{ id: 'r', name: 'Carlos' }]);
    f.componentRef.setInput('teams', [{ teamId: 't', teamName: 'Azul' }]);
    f.componentInstance.form.patchValue({
      resourceId: 'a',
      horizonStart: '2026-11-14T18:00',
      horizonEnd: '2026-11-15T02:00',
      startsAt: '2026-11-14T18:00',
      endsAt: '2026-11-14T19:00',
    });
    f.detectChanges();
    return f;
  }
  for (const kind of [
    'playingAreaUnavailable',
    'refereeUnavailable',
    'teamUnavailable',
    'matchDelayed',
  ])
    it(`envia somente os campos do incidente ${kind}`, () => {
      const f = setup(),
        c = f.componentInstance;
      (f.nativeElement.querySelector(`input[value="${kind}"]`) as HTMLInputElement).click();
      c.form.patchValue({ matchId: 'm', resourceId: 'a' });
      f.detectChanges();
      expect(f.nativeElement.querySelector('input[formControlName="delayMinutes"]') !== null).toBe(
        kind === 'matchDelayed',
      );
      c.generate();
      const req = http.expectOne('/api/tournaments/cup/schedule/repair/generate');
      expect(req.request.body.incident.kind).toBe(kind);
      expect(req.request.body.incident.matchId).toBe(kind === 'matchDelayed' ? 'm' : undefined);
      expect(req.request.body.incident.resourceId).toBe(kind === 'matchDelayed' ? undefined : 'a');
      req.flush(proposal);
    });
  it('mostra antes/depois e métricas sem salvar, aplica somente token', () => {
    const f = setup(),
      c = f.componentInstance;
    c.generate();
    http.expectOne('/api/tournaments/cup/schedule/repair/generate').flush(proposal);
    f.detectChanges();
    for (const text of [
      'Antes',
      'Depois',
      'Azul × Verde',
      '1 partida precisa mudar',
      '2 partidas permanecem como estão',
      '120 min',
    ])
      expect(f.nativeElement.textContent).toContain(text);
    http.expectNone('/api/tournaments/cup/schedule/repair/apply');
    c.apply();
    const req = http.expectOne('/api/tournaments/cup/schedule/repair/apply');
    expect(req.request.body).toEqual({ proposalToken: 'protected' });
    req.flush({ appliedCount: 1 });
    expect(c.proposal()).toBeNull();
  });
  it('informa quando o reparo mínimo foi comprovado', () => {
    const f = setup();
    f.componentInstance.proposal.set(proposal);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('O menor conjunto de mudanças foi comprovado.');
    expect(f.nativeElement.textContent).not.toContain('não foi comprovado');
  });
  it('avisa quando o mínimo não foi comprovado e mantém a aplicação disponível', () => {
    const f = setup(),
      c = f.componentInstance;
    c.proposal.set({ ...proposal, status: 'feasible' });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain(
      'O reparo é válido, mas o menor conjunto possível de mudanças não foi comprovado dentro do tempo de cálculo.',
    );
    expect(f.nativeElement.textContent).not.toContain(
      'O menor conjunto de mudanças foi comprovado.',
    );
    const apply = [...f.nativeElement.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Aplicar solução'),
    ) as HTMLButtonElement | undefined;
    expect(apply).toBeTruthy();
    apply!.click();
    const req = http.expectOne('/api/tournaments/cup/schedule/repair/apply');
    expect(req.request.body).toEqual({ proposalToken: 'protected' });
    req.flush({ appliedCount: 1 });
    expect(c.proposal()).toBeNull();
  });
  it('descarta sem enviar mudanças', () => {
    const c = setup().componentInstance;
    c.proposal.set(proposal);
    c.discard();
    expect(c.proposal()).toBeNull();
    http.expectNone(() => true);
  });
  it('no-op não oferece aplicação', () => {
    const f = setup();
    f.componentInstance.proposal.set({
      ...proposal,
      proposalToken: null,
      changes: [],
      metrics: { ...proposal.metrics, changedCount: 0 },
    });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Nenhuma alteração é necessária');
    expect(f.nativeElement.textContent).not.toContain('Aplicar solução');
  });
  for (const status of ['infeasible', 'timeout', 'error'])
    it(`explica ${status}`, () => {
      const f = setup();
      f.componentInstance.proposal.set({ ...proposal, status, proposalToken: null, changes: [] });
      f.detectChanges();
      expect(f.nativeElement.textContent).not.toContain('Aplicar solução');
    });
  it('limpa proposta desatualizada e mantém erro visível', () => {
    const c = setup().componentInstance;
    c.proposal.set(proposal);
    c.apply();
    http
      .expectOne('/api/tournaments/cup/schedule/repair/apply')
      .flush({ detail: 'Agenda mudou' }, { status: 409, statusText: 'Conflict' });
    expect(c.proposal()).toBeNull();
    expect(c.error()).toContain('Agenda mudou');
  });

  it('mantém parâmetros técnicos recolhidos e deriva o período da agenda', () => {
    const f = setup();
    const c = f.componentInstance;
    c.form.patchValue({ horizonStart: '', horizonEnd: '' });
    const details = f.nativeElement.querySelector('.advanced-options') as HTMLDetailsElement;
    expect(details.open).toBe(false);
    c.generate();
    const request = http.expectOne('/api/tournaments/cup/schedule/repair/generate');
    expect(Date.parse(request.request.body.horizon.horizonStart)).toBe(Date.parse(row.startsAt));
    expect(Date.parse(request.request.body.horizon.horizonEnd)).toBeGreaterThan(
      Date.parse(row.startsAt),
    );
    request.flush(proposal);
  });
});
