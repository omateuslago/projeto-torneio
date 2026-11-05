import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Autopilot, Proposal } from './autopilot';

const proposal: Proposal = {
  status: 'optimal',
  proposalToken: 'protected',
  expiresAt: '2026-11-14T18:10:00Z',
  warnings: [],
  scheduledMatches: [
    { matchId: 'm', startsAt: '2026-11-14T18:00:00Z', playingAreaId: 'a', refereeId: null },
  ],
  metrics: {
    scheduledCount: 1,
    preservedCount: 2,
    startsAt: '2026-11-14T18:00:00Z',
    endsAt: '2026-11-14T19:00:00Z',
    playingAreasUsed: 1,
    refereesUsed: 0,
  },
};
describe('Tournament Autopilot', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Autopilot],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup() {
    const f = TestBed.createComponent(Autopilot);
    f.componentRef.setInput('tournamentId', 'cup');
    f.componentRef.setInput('matches', [
      { matchId: 'm', homeTeam: 'Azul', awayTeam: 'Verde', startsAt: null, matchStatus: 'Pending' },
    ]);
    f.componentRef.setInput('areas', [{ id: 'a', name: 'Quadra 1' }]);
    f.componentRef.setInput('referees', []);
    f.componentInstance.form.setValue({
      horizonStart: '2026-11-14T18:00',
      horizonEnd: '2026-11-15T02:00',
      granularityMinutes: 15,
      requireReferee: false,
    });
    f.detectChanges();
    return f;
  }
  it('calcula preview sem aplicar e exibe nomes e métricas', () => {
    const f = setup(),
      c = f.componentInstance;
    c.generate();
    expect(c.calculating()).toBe(true);
    const req = http.expectOne('/api/tournaments/cup/schedule/generate');
    expect(req.request.body.horizonStart).toMatch(/Z$/);
    req.flush(proposal);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Azul × Verde');
    expect(f.nativeElement.textContent).toContain('Quadra 1');
    expect(f.nativeElement.textContent).toContain('2 partidas preservadas');
    http.expectNone('/api/tournaments/cup/schedule/apply');
  });
  it('aplica somente o token e notifica a agenda', () => {
    const f = setup(),
      c = f.componentInstance;
    c.proposal.set(proposal);
    const emit = vi.spyOn(c.applied, 'emit');
    c.apply();
    const req = http.expectOne('/api/tournaments/cup/schedule/apply');
    expect(req.request.body).toEqual({ proposalToken: 'protected' });
    req.flush({ appliedCount: 1 });
    expect(emit).toHaveBeenCalled();
    expect(c.message()).toContain('Agenda aplicada');
    expect(c.proposal()).toBeNull();
  });
  it('descarta sem enviar alterações', () => {
    const c = setup().componentInstance;
    c.proposal.set(proposal);
    c.discard();
    expect(c.proposal()).toBeNull();
    http.expectNone(() => true);
  });
  for (const status of ['infeasible', 'timeout'] as const)
    it(`distingue ${status} e não oferece aplicação`, () => {
      const f = setup(),
        c = f.componentInstance;
      c.generate();
      http
        .expectOne('/api/tournaments/cup/schedule/generate')
        .flush({
          ...proposal,
          status,
          proposalToken: null,
          scheduledMatches: [],
          warnings: ['Amplie a disponibilidade.'],
        });
      f.detectChanges();
      expect(f.nativeElement.textContent).toContain('Amplie a disponibilidade.');
      expect(f.nativeElement.textContent).toContain(
        status === 'timeout' ? 'não significa' : 'Não existe',
      );
      expect(f.nativeElement.textContent).not.toContain('Aplicar agenda');
    });
  it('explica indisponibilidade do serviço e permite nova tentativa', () => {
    const f = setup(),
      c = f.componentInstance;
    c.generate();
    http
      .expectOne('/api/tournaments/cup/schedule/generate')
      .flush(
        { detail: 'Geração automática indisponível.' },
        { status: 503, statusText: 'Unavailable' },
      );
    expect(c.error()).toContain('indisponível');
    expect(c.calculating()).toBe(false);
    expect(c.form.controls.horizonStart.value).toBe('2026-11-14T18:00');
  });
  it('exige nova proposta quando o estado mudou', () => {
    const c = setup().componentInstance;
    c.proposal.set(proposal);
    c.apply();
    http
      .expectOne('/api/tournaments/cup/schedule/apply')
      .flush(
        { detail: 'Recursos mudaram. Gere uma nova proposta.' },
        { status: 409, statusText: 'Conflict' },
      );
    expect(c.proposal()).toBeNull();
    expect(c.error()).toContain('nova proposta');
  });
});
