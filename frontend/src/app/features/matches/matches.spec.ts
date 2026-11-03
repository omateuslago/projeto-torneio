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

  const homeSubmission = {
    id: 'sub-a',
    matchId: 'game',
    submittedByTeamId: 'a',
    homeScore: 3,
    awayScore: 1,
    submittedAt: '2026-11-03T00:00:00Z',
    updatedAt: null,
  };
  const dispute = {
    id: 'dispute',
    matchId: 'game',
    status: 'Open' as const,
    openedAt: '2026-11-03T00:00:00Z',
    resolvedAt: null,
    resolutionNote: null,
  };
  it('envia como mandante e mostra quem falta confirmar nas pendências', () => {
    const fixture = setup([match]);
    fixture.componentInstance.sendAs(match, 'a');
    fixture.componentInstance.form.setValue({ homeScore: 3, awayScore: 1 });
    fixture.componentInstance.saveResult();
    const request = http.expectOne('/api/matches/game/score-submissions/a');
    expect(request.request.body).toEqual({ homeScore: 3, awayScore: 1 });
    request.flush(homeSubmission);
    flushView([{ ...match, status: 'AwaitingConfirmation', submissions: [homeSubmission] }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Pendências (1)');
    expect(fixture.nativeElement.textContent).toContain('Aguardando resultado de Verde');
    expect(fixture.componentInstance.matches()[0].homeScore).toBeNull();
  });
  it('envia como visitante e exibe confirmação, placar oficial e classificação', () => {
    const awaiting: Match = {
      ...match,
      status: 'AwaitingConfirmation',
      submissions: [homeSubmission],
    };
    const fixture = setup([awaiting]);
    fixture.componentInstance.sendAs(awaiting, 'b');
    fixture.componentInstance.form.setValue({ homeScore: 3, awayScore: 1 });
    fixture.componentInstance.saveResult();
    http
      .expectOne('/api/matches/game/score-submissions/b')
      .flush({ ...homeSubmission, id: 'sub-b', submittedByTeamId: 'b' });
    flushView(
      [
        {
          ...awaiting,
          status: 'Finished',
          homeScore: 3,
          awayScore: 1,
          resultSource: 'Bilateral',
          submissions: [homeSubmission, { ...homeSubmission, id: 'sub-b', submittedByTeamId: 'b' }],
        },
      ],
      [row],
      { ...tournament, status: 'Finished' },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Resultado confirmado pelas equipes');
    expect(fixture.nativeElement.textContent).toContain('Pendências (0)');
    expect(fixture.componentInstance.standings()[0].points).toBe(3);
  });
  it('exibe disputa quando a segunda submissão diverge sem resultado oficial', () => {
    const awaiting: Match = {
      ...match,
      status: 'AwaitingConfirmation',
      submissions: [homeSubmission],
    };
    const fixture = setup([awaiting]);
    fixture.componentInstance.sendAs(awaiting, 'b');
    fixture.componentInstance.form.setValue({ homeScore: 2, awayScore: 1 });
    fixture.componentInstance.saveResult();
    http.expectOne('/api/matches/game/score-submissions/b').flush({});
    flushView([
      {
        ...awaiting,
        status: 'Disputed',
        dispute,
        submissions: [
          homeSubmission,
          { ...homeSubmission, id: 'sub-b', submittedByTeamId: 'b', homeScore: 2 },
        ],
      },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Resultado contestado');
    expect(fixture.nativeElement.textContent).toContain('Resolver disputa');
    expect(fixture.nativeElement.textContent).toContain('2 × 1');
    expect(fixture.componentInstance.matches()[0].homeScore).toBeNull();
  });
  it('exige motivo para resolver e atualiza o resultado e as pendências', () => {
    const disputed: Match = { ...match, status: 'Disputed', dispute };
    const fixture = setup([disputed]);
    fixture.componentInstance.resolve(disputed);
    fixture.componentInstance.saveResult();
    http.expectNone('/api/disputes/dispute/resolve');
    fixture.componentInstance.resolutionNote.setValue('Súmula conferida.');
    fixture.componentInstance.form.setValue({ homeScore: 3, awayScore: 1 });
    fixture.componentInstance.saveResult();
    const request = http.expectOne('/api/disputes/dispute/resolve');
    expect(request.request.body).toEqual({
      homeScore: 3,
      awayScore: 1,
      resolutionNote: 'Súmula conferida.',
    });
    request.flush({});
    flushView(
      [
        {
          ...disputed,
          status: 'Finished',
          homeScore: 3,
          awayScore: 1,
          resultSource: 'DisputeResolution',
          dispute: { ...dispute, status: 'Resolved', resolutionNote: 'Súmula conferida.' },
        },
      ],
      [row],
      { ...tournament, status: 'Finished' },
    );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Disputa resolvida');
    expect(fixture.nativeElement.textContent).toContain('Súmula conferida.');
    expect(fixture.componentInstance.pending()).toHaveLength(0);
  });
  it('mostra identidade não autenticada e esconde envio em partida finalizada', () => {
    const fixture = setup([match]);
    expect(fixture.nativeElement.textContent).toContain('MVP sem login');
    fixture.componentInstance.load();
    flushView([{ ...match, status: 'Finished', homeScore: 0, awayScore: 0 }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Enviar como');
    expect(fixture.nativeElement.textContent).toContain('Ação administrativa');
  });
  it('mantém placar e equipe selecionada após falha no envio', () => {
    const fixture = setup([match]);
    fixture.componentInstance.sendAs(match, 'a');
    fixture.componentInstance.form.setValue({ homeScore: 3, awayScore: 1 });
    fixture.componentInstance.saveResult();
    http
      .expectOne('/api/matches/game/score-submissions/a')
      .flush(
        { detail: 'Official result is already final.' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('O resultado já foi confirmado');
    expect(fixture.componentInstance.submittingTeam()).toBe('a');
    expect(fixture.componentInstance.form.value.homeScore).toBe(3);
  });
});
