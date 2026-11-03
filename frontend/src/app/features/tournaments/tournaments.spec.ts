import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { Tournaments } from './tournaments';

describe('Criação de campeonatos', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [Tournaments], providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])] });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  function setup() {
    const fixture = TestBed.createComponent(Tournaments);
    http.expectOne('/api/tournaments').flush([]);
    fixture.detectChanges();
    return fixture;
  }
  it('não envia nomes vazios ou capacidade fracionária', () => {
    const fixture = setup();
    fixture.componentInstance.form.setValue({ name: '   ', sport: 'Futsal', maxTeams: 2.5, format: 'RoundRobin' });
    fixture.componentInstance.create();
    fixture.detectChanges();
    http.expectNone(request => request.method === 'POST');
    expect(fixture.nativeElement.textContent).toContain('número inteiro');
  });
  it('envia o DTO e navega apenas após a criação confirmada', () => {
    const fixture = setup();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.componentInstance.form.setValue({ name: ' Copa ', sport: ' Futsal ', maxTeams: 8, format: 'RoundRobin' });
    fixture.componentInstance.create();
    const request = http.expectOne('/api/tournaments');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ name: 'Copa', sport: 'Futsal', maxTeams: 8, format: 'RoundRobin' });
    expect(navigate).not.toHaveBeenCalled();
    request.flush({ ...request.request.body, id: 'tournament-1', status: 'Draft' });
    expect(navigate).toHaveBeenCalledWith(['/campeonatos', 'tournament-1']);
    expect(fixture.componentInstance.saving()).toBe(false);
  });
  it('preserva o formulário e permite tentar novamente após erro', () => {
    const fixture = setup();
    fixture.componentInstance.form.setValue({ name: 'Copa', sport: 'Futsal', maxTeams: 8, format: 'RoundRobin' });
    fixture.componentInstance.create();
    http.expectOne('/api/tournaments').flush({ detail: 'Falha temporária.' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Falha temporária.');
    expect(fixture.componentInstance.form.controls.name.value).toBe('Copa');
    expect(fixture.componentInstance.saving()).toBe(false);
  });
});
