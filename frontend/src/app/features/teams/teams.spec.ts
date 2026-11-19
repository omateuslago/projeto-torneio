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
});
