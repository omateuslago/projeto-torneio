import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { Tournaments } from './tournaments';
import { AuthService } from '../../core/auth.service';

describe('Criação de campeonatos', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Tournaments],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
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
  function setup(openModal = true) {
    const fixture = TestBed.createComponent(Tournaments);
    http.expectOne('/api/tournaments').flush([]);
    fixture.detectChanges();
    if (openModal) {
      fixture.componentInstance.openCreateModal();
      fixture.detectChanges();
    }
    return fixture;
  }
  it('não envia nomes vazios ou capacidade fracionária', () => {
    const fixture = setup();
    fixture.componentInstance.form.setValue({
      name: '   ',
      sport: 'Futsal',
      maxTeams: 2.5,
      format: 'RoundRobin',
    });
    fixture.componentInstance.create();
    fixture.detectChanges();
    http.expectNone((request) => request.method === 'POST');
    expect(fixture.nativeElement.textContent).toContain('deve ser inteiro');
  });

  it('oferece os dois formatos, mantém pontos corridos como padrão e aceita esporte personalizado', () => {
    const fixture = setup();
    expect(fixture.nativeElement.textContent).toContain('Pontos corridos');
    expect(fixture.nativeElement.textContent).toContain('Mata-mata simples');
    expect(fixture.componentInstance.form.controls.format.value).toBe('RoundRobin');
    fixture.componentInstance.chooseSport('Outro');
    fixture.componentInstance.form.patchValue({ name: 'Copa', sport: 'Beach tennis', maxTeams: 8 });
    fixture.componentInstance.create();
    const request = http.expectOne('/api/tournaments');
    expect(request.request.body).toEqual({
      name: 'Copa',
      sport: 'Beach tennis',
      maxTeams: 8,
      format: 'RoundRobin',
    });
    request.flush({ ...request.request.body, id: 'custom', status: 'Draft' });
  });

  it('envia SingleElimination quando o usuário escolhe mata-mata', () => {
    const fixture = setup();
    fixture.componentInstance.form.patchValue({
      name: 'Copa mata-mata',
      sport: 'Futsal',
      maxTeams: 16,
      format: 'SingleElimination',
    });
    fixture.componentInstance.create();
    const request = http.expectOne('/api/tournaments');
    expect(request.request.body.format).toBe('SingleElimination');
    request.flush({ ...request.request.body, id: 'knockout', status: 'Draft' });
  });

  it('continua renderizando campeonato mata-mata existente sem expor o formato no card', () => {
    const fixture = TestBed.createComponent(Tournaments);
    http.expectOne('/api/tournaments').flush([
      {
        id: 'legacy',
        name: 'Copa antiga',
        sport: 'Futebol',
        maxTeams: 8,
        format: 'SingleElimination',
        status: 'Draft',
      },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Copa antiga');
    expect(fixture.nativeElement.textContent).not.toContain('Mata-mata simples');
  });
  it('envia o DTO, fecha o modal e atualiza a lista após a criação confirmada', () => {
    const fixture = setup();
    fixture.componentInstance.form.setValue({
      name: ' Copa ',
      sport: ' Futsal ',
      maxTeams: 8,
      format: 'RoundRobin',
    });
    fixture.componentInstance.create();
    const request = http.expectOne('/api/tournaments');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      name: 'Copa',
      sport: 'Futsal',
      maxTeams: 8,
      format: 'RoundRobin',
    });
    request.flush({ ...request.request.body, id: 'tournament-1', status: 'Draft' });
    expect(fixture.componentInstance.tournaments()).toHaveLength(1);
    expect(fixture.componentInstance.tournaments()[0].name).toBe('Copa');
    expect(fixture.componentInstance.isCreateModalOpen()).toBe(false);
    expect(fixture.componentInstance.saving()).toBe(false);
  });
  it('preserva o formulário e permite tentar novamente após erro', () => {
    const fixture = setup();
    fixture.componentInstance.form.setValue({
      name: 'Copa',
      sport: 'Futsal',
      maxTeams: 8,
      format: 'RoundRobin',
    });
    fixture.componentInstance.create();
    http
      .expectOne('/api/tournaments')
      .flush({ detail: 'Falha temporária.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Falha temporária.');
    expect(fixture.componentInstance.form.controls.name.value).toBe('Copa');
    expect(fixture.componentInstance.saving()).toBe(false);
  });

  it('exibe o estado vazio somente após uma resposta bem-sucedida sem campeonatos', () => {
    const fixture = setup(false);

    expect(fixture.nativeElement.textContent).toContain('Nenhum campeonato por aqui');
    expect(fixture.nativeElement.querySelector('img')?.getAttribute('src')).toBe(
      'assests/brakvyo-trofeu-outline.svg',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Não foi possível concluir');
  });

  it('exibe o erro da API e permite tentar carregar novamente', () => {
    const fixture = TestBed.createComponent(Tournaments);
    const initialRequest = http.expectOne('/api/tournaments');
    initialRequest.flush(
      { detail: 'Falha temporária.' },
      { status: 503, statusText: 'Unavailable' },
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Não foi possível carregar os campeonatos');
    expect(fixture.nativeElement.textContent).toContain('Ocorreu um problema. Tente novamente.');
    expect(
      fixture.nativeElement.querySelector('.tournaments-state--error img')?.getAttribute('src'),
    ).toBe('assests/brakvyo-trofeu-outline.svg');
    expect(fixture.nativeElement.textContent).toContain('Falha temporária.');
    expect(fixture.nativeElement.textContent).not.toContain('Nenhum campeonato por aqui');

    fixture.componentInstance.load();
    const retryRequest = http.expectOne('/api/tournaments');
    retryRequest.flush([
      {
        id: 'tournament-1',
        name: 'Copa',
        sport: 'Futsal',
        maxTeams: 8,
        format: 'RoundRobin',
        status: 'Draft',
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Copa');
    expect(fixture.nativeElement.textContent).not.toContain('Falha temporária.');
  });
});
