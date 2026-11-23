import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Teams } from './teams';
import { AuthService } from '../../core/auth.service';

describe('Equipes', () => {
  it('mantém a gestão de acesso recolhida até o manager solicitar', () => {
    TestBed.configureTestingModule({
      imports: [Teams],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(AuthService).user.set({
      authenticated: true,
      id: 'manager',
      email: 'manager@example.com',
      displayName: 'Manager',
      isPlatformAdmin: false,
      organizerTournamentIds: [],
      managedTeamIds: ['team'],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Teams);
    http.expectOne('/api/teams').flush([{ id: 'team', name: 'Azul' }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-access-management')).toBeNull();
    const button = [...fixture.nativeElement.querySelectorAll('button')].find(
      (item: HTMLButtonElement) => item.textContent?.includes('Gerenciar acesso'),
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-access-management')).toBeTruthy();
    http.expectOne('/api/teams/team/managers').flush({ members: [], pendingInvitations: [] });
    http.verify();
  });

  it('abre e fecha o modal e mantém o fluxo existente de criação', () => {
    TestBed.configureTestingModule({
      imports: [Teams],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(AuthService).user.set({
      authenticated: true,
      id: 'organizer',
      email: 'organizer@example.com',
      displayName: 'Organizer',
      isPlatformAdmin: false,
      organizerTournamentIds: [],
      managedTeamIds: [],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(Teams);
    http.expectOne('/api/teams').flush([]);
    fixture.detectChanges();

    const trigger = fixture.nativeElement.querySelector('.page-header button') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeTruthy();

    fixture.nativeElement
      .querySelector('[role="dialog"]')
      .dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();

    trigger.click();
    fixture.detectChanges();
    fixture.componentInstance.form.setValue({ name: 'Panteras' });
    fixture.componentInstance.create();
    const create = http.expectOne('/api/teams');
    expect(create.request.body).toEqual({ name: 'Panteras' });
    create.flush({ id: 'new-team', name: 'Panteras' });
    http.expectOne('/api/teams').flush([{ id: 'new-team', name: 'Panteras' }]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Equipe Panteras cadastrada.');
    expect(fixture.nativeElement.querySelector('.team-card')?.textContent).toContain('Panteras');
    http.verify();
  });
});
