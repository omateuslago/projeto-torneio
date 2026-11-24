import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { TournamentDetail } from './tournament-detail';
import { Tournament, TournamentStatus } from '../../core/models';
import { AuthService } from '../../core/auth.service';

const tournament: Tournament = {
  id: 'cup',
  name: 'Copa',
  sport: 'Futsal',
  maxTeams: 2,
  format: 'RoundRobin',
  status: 'Draft',
};

describe('Atualizações do campeonato', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TournamentDetail],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { paramMap: of(convertToParamMap({ id: 'cup' })) },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('ignora refresh atrasado após abrir inscrições', () => {
    const component = TestBed.createComponent(TournamentDetail).componentInstance;
    http.expectOne('/api/tournaments/cup').flush(tournament);
    component.changeStatus('Open');
    http.expectOne('/api/tournaments/cup/status').flush({ ...tournament, status: 'Open' });
    component.updateTournament(tournament);
    expect(component.tournament()?.status).toBe('Open');
  });

  it('aceita conclusão e ignora resposta de outro campeonato', () => {
    const component = TestBed.createComponent(TournamentDetail).componentInstance;
    http.expectOne('/api/tournaments/cup').flush(tournament);
    component.updateTournament({ ...tournament, status: 'Finished' });
    component.updateTournament({ ...tournament, id: 'another', status: 'Finished' });
    expect(component.tournament()).toEqual({ ...tournament, status: 'Finished' });
  });

  it.each([
    ['Draft', 'Prepare seu campeonato', 'Preparação', 0],
    ['Open', 'Inscrições abertas', 'Equipes', 1],
    ['Closed', 'O próximo passo é gerar os confrontos', 'Jogos', 2],
    ['InProgress', 'Campeonato em andamento', 'Em andamento', 3],
    ['Finished', 'Campeonato encerrado', 'Finalizado', 4],
  ] as [TournamentStatus, string, string, number][])(
    'prioriza a etapa %s na apresentação',
    (status, copy, step, completed) => {
      const fixture = TestBed.createComponent(TournamentDetail);
      const current = { ...tournament, status };
      http.expectOne('/api/tournaments/cup').flush(current);
      fixture.detectChanges();
      http.expectOne('/api/tournaments/cup/registrations').flush([]);
      http.expectOne('/api/teams').flush([]);
      if (status === 'Closed' || status === 'InProgress' || status === 'Finished') {
        http.expectOne('/api/tournaments/cup/matches').flush([]);
        http.expectOne('/api/tournaments/cup/standings').flush([]);
        http.expectOne('/api/tournaments/cup').flush(current);
      }
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain(copy);
      const progress = fixture.nativeElement.querySelector(
        '[aria-label="Progresso do campeonato"]',
      );
      expect(progress.querySelector('[aria-current="step"]')?.textContent).toContain(step);
      expect(progress.querySelectorAll('.tournament-progress__step--complete')).toHaveLength(
        completed,
      );
      if (status === 'Closed') {
        expect(fixture.nativeElement.textContent).not.toContain('Gerar confrontos');
      }
    },
  );

  it('oferece geração da chave sem destacar agenda ou classificação para mata-mata fechado', () => {
    TestBed.inject(AuthService).user.set({
      authenticated: true,
      id: 'organizer',
      email: 'organizer@example.com',
      displayName: 'Organizer',
      isPlatformAdmin: true,
      organizerTournamentIds: [],
      managedTeamIds: [],
    });
    const fixture = TestBed.createComponent(TournamentDetail);
    const legacy = {
      ...tournament,
      format: 'SingleElimination' as const,
      status: 'Closed' as const,
    };
    http.expectOne('/api/tournaments/cup').flush(legacy);
    fixture.detectChanges();
    http
      .expectOne('/api/tournaments/cup/organizers')
      .flush({ members: [], pendingInvitations: [] });
    http.expectOne('/api/tournaments/cup/registrations').flush([]);
    http.expectOne('/api/teams').flush([]);
    http
      .expectOne('/api/tournaments/cup/bracket')
      .flush({ tournamentId: 'cup', status: 'Closed', rounds: [], champion: null });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Gerar chave');
    expect(fixture.nativeElement.textContent).not.toContain('Gerar confrontos');
    expect(fixture.nativeElement.textContent).not.toContain('Abrir agenda');
    expect(fixture.nativeElement.textContent).not.toContain('Ver classificação final');
    http.expectNone('/api/tournaments/cup/matches');
  });
});
