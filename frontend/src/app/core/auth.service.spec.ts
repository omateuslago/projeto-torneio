import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthService, CurrentUser } from './auth.service';

const user: CurrentUser = {
  authenticated: true,
  id: 'user-1',
  email: 'mateus@example.com',
  displayName: 'Mateus',
  isPlatformAdmin: false,
  organizerTournamentIds: ['cup'],
  managedTeamIds: ['falcons'],
};

describe('AuthService', () => {
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('carrega a sessão uma vez no estado central', () => {
    service.refresh().subscribe();
    http.expectOne('/api/auth/csrf').flush({ token: 'csrf' });
    http.expectOne('/api/auth/me').flush(user);

    expect(service.loaded()).toBe(true);
    expect(service.isAuthenticated()).toBe(true);
    expect(service.canManageTournament('cup')).toBe(true);
    expect(service.canManageTeam('other')).toBe(false);
    service.recordCreatedTournament('new-cup');
    service.recordCreatedTeam('new-team');
    expect(service.canManageTournament('new-cup')).toBe(true);
    expect(service.canManageTeam('new-team')).toBe(true);
  });

  it('faz login e renova o antiforgery para a identidade autenticada', () => {
    service.login('mateus@example.com', 'password', true).subscribe();
    http.expectOne('/api/auth/csrf').flush({ token: 'anonymous-csrf' });
    const login = http.expectOne('/api/auth/login');
    expect(login.request.body).toEqual({
      email: 'mateus@example.com',
      password: 'password',
      rememberMe: true,
    });
    login.flush(user);
    http.expectOne('/api/auth/csrf').flush({ token: 'authenticated-csrf' });

    expect(service.user()).toEqual(user);
  });

  it('registra sem enviar role e limpa a sessão no logout', () => {
    service.register('mateus@example.com', 'password', 'Mateus').subscribe();
    http.expectOne('/api/auth/csrf').flush({ token: 'anonymous-csrf' });
    const register = http.expectOne('/api/auth/register');
    expect(register.request.body).toEqual({
      email: 'mateus@example.com',
      password: 'password',
      displayName: 'Mateus',
    });
    register.flush(user);
    http.expectOne('/api/auth/csrf').flush({ token: 'authenticated-csrf' });

    service.logout().subscribe();
    http.expectOne('/api/auth/logout').flush(null);
    expect(service.isAuthenticated()).toBe(false);
  });
});
