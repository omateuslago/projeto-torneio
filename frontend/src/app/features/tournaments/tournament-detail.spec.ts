import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { TournamentDetail } from './tournament-detail';
import { Tournament } from '../../core/models';

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
});
