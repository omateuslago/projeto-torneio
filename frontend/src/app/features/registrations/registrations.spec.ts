import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Registrations } from './registrations';
import { Tournament, TournamentStatus } from '../../core/models';

describe('Inscrições', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Registrations],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function flushLists() {
    http.expectOne('/api/tournaments/cup/registrations').flush([]);
    http.expectOne('/api/teams').flush([{ id: 'blue', name: 'Azul' }]);
  }
  function setup(status: TournamentStatus = 'Open') {
    const fixture = TestBed.createComponent(Registrations);
    fixture.componentRef.setInput('tournament', {
      id: 'cup',
      name: 'Copa',
      sport: 'Futsal',
      maxTeams: 2,
      format: 'RoundRobin',
      status,
    } satisfies Tournament);
    fixture.detectChanges();
    flushLists();
    fixture.detectChanges();
    return fixture;
  }
  it.each([
    ['Team is already registered.', 'já está inscrita'],
    ['Tournament is full.', 'campeonato está lotado'],
    ['Registration is not open.', 'estão fechadas'],
  ])('exibe a resposta de conflito %s', (detail, expected) => {
    const fixture = setup();
    fixture.componentInstance.form.setValue({ teamId: 'blue' });
    fixture.componentInstance.register();
    const request = http.expectOne('/api/tournaments/cup/registrations');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ teamId: 'blue' });
    request.flush({ detail }, { status: 409, statusText: 'Conflict' });
    flushLists();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(expected);
    expect(fixture.componentInstance.saving()).toBe(false);
  });
  it('não permite inscrição quando o campeonato está fechado', () => {
    const fixture = setup('Closed');
    fixture.componentInstance.form.setValue({ teamId: 'blue' });
    fixture.componentInstance.register();
    http.expectNone((request) => request.method === 'POST');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });
  it('mostra falha de carregamento e permite atualizar', () => {
    const fixture = setup();
    fixture.componentInstance.load();
    http.expectOne('/api/teams').flush([]);
    http
      .expectOne('/api/tournaments/cup/registrations')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
    fixture.componentInstance.load();
    flushLists();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
