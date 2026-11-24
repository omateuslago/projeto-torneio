import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Bracket } from './bracket';
import { AuthService } from '../../core/auth.service';
import { Tournament } from '../../core/models';

const tournament: Tournament = {
  id: 'cup',
  name: 'Copa eliminatória',
  sport: 'Futsal',
  maxTeams: 8,
  format: 'SingleElimination',
  status: 'Closed',
};

const generatedBracket = {
  tournamentId: 'cup',
  status: 'InProgress',
  champion: null,
  rounds: [
    {
      number: 1,
      name: 'Semifinais',
      nodes: [
        {
          id: 'semi-1',
          position: 1,
          topTeam: { id: 'a', name: 'Azul' },
          bottomTeam: { id: 'b', name: 'Branco' },
          isBye: false,
          match: {
            id: 'match-1',
            status: 'Finished',
            topScore: 2,
            bottomScore: 0,
            winnerTeamId: 'a',
            startsAt: null,
            playingAreaId: null,
          },
        },
        {
          id: 'semi-2',
          position: 2,
          topTeam: { id: 'c', name: 'Ciano' },
          bottomTeam: { id: 'd', name: 'Dourado' },
          isBye: false,
          match: {
            id: 'match-2',
            status: 'Pending',
            topScore: null,
            bottomScore: null,
            winnerTeamId: null,
            startsAt: null,
            playingAreaId: null,
          },
        },
      ],
    },
    {
      number: 2,
      name: 'Final',
      nodes: [
        {
          id: 'final',
          position: 1,
          topTeam: { id: 'a', name: 'Azul' },
          bottomTeam: null,
          isBye: false,
          match: null,
        },
      ],
    },
  ],
};

describe('Chave eliminatória', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Bracket],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(AuthService).user.set({
      authenticated: true,
      id: 'admin',
      email: 'admin@example.com',
      displayName: 'Admin',
      isPlatformAdmin: true,
      organizerTournamentIds: [],
      managedTeamIds: [],
    });
  });

  afterEach(() => http.verify());

  it('apresenta fases, conexões, vencedor e slots futuros semanticamente', () => {
    const fixture = TestBed.createComponent(Bracket);
    fixture.componentRef.setInput('tournament', tournament);
    fixture.detectChanges();
    http.expectOne('/api/tournaments/cup/bracket').flush(generatedBracket);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Semifinais');
    expect(text).toContain('Final');
    expect(text).toContain('Azul');
    expect(text).toContain('A definir');
    expect(fixture.nativeElement.querySelectorAll('ol').length).toBe(2);
    expect(fixture.nativeElement.querySelectorAll('.bracket-node--connected').length).toBe(2);
    expect(fixture.nativeElement.querySelector('.bracket-team--winner')?.textContent).toContain(
      'Azul',
    );
    expect(fixture.nativeElement.querySelector('.bracket-card__action')?.getAttribute('href')).toBe(
      '#match-match-2',
    );
  });

  it('gera a chave fechada e mantém a operação idempotente no mesmo endpoint', () => {
    const fixture = TestBed.createComponent(Bracket);
    fixture.componentRef.setInput('tournament', tournament);
    fixture.detectChanges();
    http
      .expectOne('/api/tournaments/cup/bracket')
      .flush({ tournamentId: 'cup', status: 'Closed', rounds: [], champion: null });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Gerar chave');
    fixture.componentInstance.generate();
    http.expectOne('/api/tournaments/cup/matches/generate').flush([]);
    http.expectOne('/api/tournaments/cup/bracket').flush(generatedBracket);
    http.expectOne('/api/tournaments/cup').flush({ ...tournament, status: 'InProgress' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Chave gerada');
  });

  it('não oferece geração para quem não administra o campeonato', () => {
    TestBed.inject(AuthService).user.set({
      authenticated: true,
      id: 'viewer',
      email: 'viewer@example.com',
      displayName: 'Viewer',
      isPlatformAdmin: false,
      organizerTournamentIds: [],
      managedTeamIds: [],
    });
    const fixture = TestBed.createComponent(Bracket);
    fixture.componentRef.setInput('tournament', tournament);
    fixture.detectChanges();
    http
      .expectOne('/api/tournaments/cup/bracket')
      .flush({ tournamentId: 'cup', status: 'Closed', rounds: [], champion: null });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Gerar chave');
  });

  it('destaca o campeão sem exibir classificação de pontos corridos', () => {
    const fixture = TestBed.createComponent(Bracket);
    fixture.componentRef.setInput('tournament', { ...tournament, status: 'Finished' });
    fixture.detectChanges();
    http
      .expectOne('/api/tournaments/cup/bracket')
      .flush({ ...generatedBracket, status: 'Finished', champion: { id: 'a', name: 'Azul' } });
    fixture.detectChanges();
    http.expectOne('/api/tournaments/cup/matches').flush([]);
    http.expectOne('/api/tournaments/cup').flush({ ...tournament, status: 'Finished' });
    http.expectOne('/api/tournaments/cup/bracket').flush({
      ...generatedBracket,
      status: 'Finished',
      champion: { id: 'a', name: 'Azul' },
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Campeão');
    expect(fixture.nativeElement.textContent).not.toContain('Classificação');
    http.expectNone('/api/tournaments/cup/standings');
  });
});
