import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Scheduling } from './scheduling';
import { ScheduledMatch } from './scheduling-api';

const match: ScheduledMatch = {
  matchId: 'm',
  homeTeamId: 'a',
  homeTeam: 'Azul',
  awayTeamId: 'b',
  awayTeam: 'Verde',
  startsAt: null,
  endsAt: null,
  playingAreaId: null,
  playingArea: null,
  venue: null,
  refereeId: null,
  referee: null,
  matchStatus: 'Pending',
};
const saved: ScheduledMatch = {
  ...match,
  startsAt: '2026-11-14T18:00:00Z',
  endsAt: '2026-11-14T19:00:00Z',
  playingAreaId: 'area',
  playingArea: 'Quadra 1',
  venue: 'Ginásio',
};
describe('Agenda e disponibilidade', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Scheduling],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'cup' }) } },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup(matches: ScheduledMatch[] = [match]) {
    const fixture = TestBed.createComponent(Scheduling);
    http.expectOne('/api/tournaments/cup').flush({ id: 'cup', name: 'Copa' });
    http
      .expectOne('/api/tournaments/cup/registrations')
      .flush([{ id: 'reg', teamId: 'a', teamName: 'Azul' }]);
    http
      .expectOne('/api/tournaments/cup/schedule-settings')
      .flush({ matchDurationMinutes: 60, timeZoneId: 'UTC' });
    http
      .expectOne('/api/tournaments/cup/venues')
      .flush([{ id: 'venue', name: 'Ginásio', address: 'Rua', active: true }]);
    http
      .expectOne('/api/tournaments/cup/playing-areas')
      .flush([{ id: 'area', venueId: 'venue', name: 'Quadra 1', active: true }]);
    http.expectOne('/api/tournaments/cup/referees').flush([]);
    http.expectOne('/api/tournaments/cup/schedule').flush(matches);
    fixture.detectChanges();
    return fixture;
  }
  it('cria local e área vinculada usando a API real', () => {
    const f = setup(),
      c = f.componentInstance;
    c.venueForm.setValue({ name: 'Arena', address: 'Centro' });
    c.createVenue();
    const v = http.expectOne('/api/tournaments/cup/venues');
    expect(v.request.body).toEqual({ name: 'Arena', address: 'Centro' });
    v.flush({ id: 'new', name: 'Arena', address: 'Centro', active: true });
    c.areaForm.setValue({ venueId: 'new', name: 'Quadra 2' });
    c.createArea();
    http
      .expectOne('/api/tournaments/cup/venues/new/playing-areas')
      .flush({ id: 'two', venueId: 'new', name: 'Quadra 2', active: true });
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Quadra 2');
  });
  it('cadastra árbitro e permite selecioná-lo na disponibilidade', () => {
    const f = setup(),
      c = f.componentInstance;
    c.refereeForm.setValue({ name: 'Carlos' });
    c.createReferee();
    http
      .expectOne('/api/tournaments/cup/referees')
      .flush({ id: 'r', name: 'Carlos', active: true });
    c.chooseKind('referees');
    expect(c.options()[0].name).toBe('Carlos');
  });
  it('adiciona, edita e remove períodos persistidos', () => {
    const f = setup(),
      c = f.componentInstance;
    c.chooseResource('a');
    http.expectOne('/api/tournaments/cup/teams/a/availability').flush([]);
    const w = { id: 'w', startsAt: '2026-11-14T08:00:00Z', endsAt: '2026-11-14T22:00:00Z' };
    c.windowForm.setValue({ startsAt: c.local(w.startsAt), endsAt: c.local(w.endsAt) });
    c.saveWindow();
    const post = http.expectOne('/api/tournaments/cup/teams/a/availability');
    expect(post.request.body.startsAt).toBe(w.startsAt.replace('Z', '.000Z'));
    post.flush(w);
    http.expectOne('/api/tournaments/cup/teams/a/availability').flush([w]);
    c.editWindow(w);
    c.saveWindow();
    http.expectOne('/api/tournaments/cup/teams/a/availability/w').flush(w);
    http.expectOne('/api/tournaments/cup/teams/a/availability').flush([w]);
    c.deleteWindow(w);
    const del = http.expectOne('/api/tournaments/cup/teams/a/availability/w');
    expect(del.request.method).toBe('DELETE');
    del.flush(null);
    expect(c.windows()).toEqual([]);
  });
  it('agenda, remove pendência e permite reagendar', () => {
    const f = setup(),
      c = f.componentInstance;
    expect(f.nativeElement.textContent).toContain('Partidas aguardando agendamento (1)');
    c.editMatch(match);
    c.matchForm.setValue({
      startsAt: c.local(saved.startsAt!),
      playingAreaId: 'area',
      refereeId: '',
    });
    c.saveMatch();
    const req = http.expectOne('/api/matches/m/schedule');
    expect(req.request.body.refereeId).toBeNull();
    req.flush(saved);
    expect(c.unscheduled()).toHaveLength(0);
    c.editMatch(saved);
    c.saveMatch();
    http.expectOne('/api/matches/m/schedule').flush({ ...saved, startsAt: '2026-11-15T18:00:00Z' });
    expect(c.matches()[0].startsAt).toContain('2026-11-15');
  });
  it('mostra conflito do backend e mantém o formulário para correção', () => {
    const f = setup(),
      c = f.componentInstance;
    c.editMatch(match);
    c.matchForm.setValue({
      startsAt: c.local(saved.startsAt!),
      playingAreaId: 'area',
      refereeId: '',
    });
    c.saveMatch();
    http
      .expectOne('/api/matches/m/schedule')
      .flush(
        { detail: 'Quadra 1 já possui partida neste horário.' },
        { status: 409, statusText: 'Conflict' },
      );
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Quadra 1 já possui partida neste horário.');
    expect(c.editingMatch()?.matchId).toBe('m');
    expect(c.matchForm.controls.playingAreaId.value).toBe('area');
    expect(c.unscheduled()).toHaveLength(1);
  });
  it('agrupa agenda por data no fuso configurado e ordena horários', () => {
    const f = setup([{ ...saved, matchId: 'late', startsAt: '2026-11-15T01:00:00Z' }, saved]);
    const c = f.componentInstance;
    c.zone.set('America/Sao_Paulo');
    expect(c.groups()).toHaveLength(1);
    expect(c.groups()[0].matches.map((x) => x.matchId)).toEqual(['m', 'late']);
  });
  it('ignora resposta atrasada da seleção anterior', () => {
    const c = setup().componentInstance;
    c.chooseResource('a');
    const first = http.expectOne('/api/tournaments/cup/teams/a/availability');
    c.chooseKind('playing-areas');
    c.chooseResource('area');
    http.expectOne('/api/tournaments/cup/playing-areas/area/availability').flush([]);
    first.flush([{ id: 'old', startsAt: saved.startsAt, endsAt: saved.endsAt }]);
    expect(c.windows()).toEqual([]);
  });
});
