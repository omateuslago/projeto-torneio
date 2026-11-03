import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Matches } from './matches';
import { Match, Standing, Tournament } from '../../core/models';

const tournament: Tournament = {
  id: 'cup',
  name: 'Copa',
  sport: 'Futsal',
  maxTeams: 2,
  format: 'RoundRobin',
  status: 'Closed',
};
const match: Match = {
  id: 'game',
  tournamentId: 'cup',
  homeTeamId: 'a',
  homeTeamName: 'Azul',
  awayTeamId: 'b',
  awayTeamName: 'Verde',
  status: 'Pending',
  homeScore: null,
  awayScore: null,
  createdAt: '2026-11-03T00:00:00Z',
  finishedAt: null,
};
const row: Standing = {
  teamId: 'a',
  teamName: 'Azul',
  played: 1,
  wins: 1,
  draws: 0,
  losses: 0,
  goalsFor: 3,
  goalsAgainst: 1,
  goalDifference: 2,
  points: 3,
};

describe('Partidas e classificação', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Matches],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function flushView(matches: Match[] = [], rows: Standing[] = [], current = tournament) {
    http.expectOne('/api/tournaments/cup/matches').flush(matches);
    http.expectOne('/api/tournaments/cup/standings').flush(rows);
    http.expectOne('/api/tournaments/cup').flush(current);
  }
  function setup(matches: Match[] = []) {
    const fixture = TestBed.createComponent(Matches);
    fixture.componentRef.setInput('tournament', tournament);
    fixture.detectChanges();
    flushView(matches);
    fixture.detectChanges();
    return fixture;
  }
  it('gera confrontos, exibe partidas e informa o novo estado ao pai', () => {
    const fixture = setup();
    const emit = vi.spyOn(fixture.componentInstance.tournamentChanged, 'emit');
    fixture.componentInstance.generate();
    const request = http.expectOne('/api/tournaments/cup/matches/generate');
    expect(request.request.method).toBe('POST');
    request.flush([match]);
    flushView([match], [], { ...tournament, status: 'InProgress' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Azul × Verde');
    expect(fixture.nativeElement.textContent).toContain('Pendente');
    expect(emit).toHaveBeenCalledWith(expect.objectContaining({ status: 'InProgress' }));
  });
  it('registra resultado e recarrega a classificação calculada pela API', () => {
    const fixture = setup([match]);
    fixture.componentInstance.edit(match);
    fixture.componentInstance.form.setValue({ homeScore: 3, awayScore: 1 });
    fixture.componentInstance.saveResult();
    const request = http.expectOne('/api/matches/game/result');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ homeScore: 3, awayScore: 1 });
    const finished: Match = {
      ...match,
      homeScore: 3,
      awayScore: 1,
      status: 'Finished',
      finishedAt: '2026-11-03T01:00:00Z',
    };
    request.flush(finished);
    flushView([finished], [row], { ...tournament, status: 'Finished' });
    fixture.detectChanges();
    expect(fixture.componentInstance.standings()[0].points).toBe(3);
    expect(
      fixture.nativeElement.querySelector('table[aria-label="Classificação do campeonato"] tbody')
        .textContent,
    ).toContain('Azul');
    expect(fixture.nativeElement.textContent).toContain('3 × 1');
    expect(fixture.componentInstance.editing()).toBeNull();
  });
  it.each([-1, 1.5])('não envia placar inválido %s', (score) => {
    const fixture = setup([match]);
    fixture.componentInstance.edit(match);
    fixture.componentInstance.form.setValue({ homeScore: score, awayScore: 0 });
    fixture.componentInstance.saveResult();
    fixture.detectChanges();
    http.expectNone((request) => request.method === 'PUT');
    expect(fixture.nativeElement.textContent).toContain('Informe placares inteiros');
  });
  it('exibe conflito ao tentar gerar em estado inválido', () => {
    const fixture = setup();
    fixture.componentInstance.generate();
    http
      .expectOne('/api/tournaments/cup/matches/generate')
      .flush(
        { detail: 'Close registration before generating matches.' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Encerre as inscrições');
    expect(fixture.componentInstance.saving()).toBe(false);
  });
  it('preserva a edição quando salvar falha', () => {
    const fixture = setup([match]);
    fixture.componentInstance.edit(match);
    fixture.componentInstance.form.setValue({ homeScore: 3, awayScore: 1 });
    fixture.componentInstance.saveResult();
    http
      .expectOne('/api/matches/game/result')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    expect(fixture.componentInstance.editing()?.id).toBe('game');
    expect(fixture.componentInstance.form.getRawValue()).toEqual({ homeScore: 3, awayScore: 1 });
    expect(fixture.componentInstance.saving()).toBe(false);
  });
});
