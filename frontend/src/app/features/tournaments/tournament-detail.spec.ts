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
    ['Draft', 'Prepare seu campeonato'],
    ['Open', 'Inscrições abertas'],
    ['Closed', 'O próximo passo é gerar os confrontos'],
    ['InProgress', 'Campeonato em andamento'],
    ['Finished', 'Campeonato encerrado'],
  ] as [TournamentStatus, string][])('prioriza a etapa %s na apresentação', (status, copy) => {
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
  });

  it('não oferece ações incompatíveis para SingleElimination fechado', () => {
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
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'O gerenciamento automático de confrontos ainda não está disponível para este formato.',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Gerar confrontos');
    expect(fixture.nativeElement.textContent).not.toContain('Abrir agenda');
    http.expectNone('/api/tournaments/cup/matches');
  });
});
