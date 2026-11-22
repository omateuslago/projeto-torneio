import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { of } from 'rxjs';
import { App } from './app';
import { AuthService, CurrentUser } from './core/auth.service';

describe('header autenticado', () => {
  const authenticated = signal(false);
  const user = signal<CurrentUser>({
    authenticated: false,
    id: null,
    email: null,
    displayName: null,
    isPlatformAdmin: false,
    organizerTournamentIds: [],
    managedTeamIds: [],
  });
  const auth = {
    isAuthenticated: authenticated,
    user,
    refresh: vi.fn(() => of(user())),
    logout: vi.fn(() => of(undefined)),
  };

  beforeEach(() => {
    authenticated.set(false);
    auth.refresh.mockClear();
    auth.logout.mockClear();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });

  it('mostra Entrar para visitante e carrega a sessão central', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(auth.refresh).toHaveBeenCalledOnce();
    expect(fixture.nativeElement.textContent).toContain('Entrar');
  });

  it('abre e fecha o menu móvel com estado acessível', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const toggle = fixture.nativeElement.querySelector('.menu-toggle') as HTMLButtonElement;
    expect(toggle.getAttribute('aria-expanded')).toBe('false');
    toggle.click();
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.querySelector('#main-navigation').classList).toContain('is-open');
  });

  it('mostra nome e encerra a sessão pelo header', () => {
    authenticated.set(true);
    user.set({ ...user(), authenticated: true, displayName: 'Mateus' });
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Mateus');
    expect(fixture.nativeElement.textContent).toContain('Notificações');
    const account = fixture.nativeElement.querySelector(
      '.account-menu__trigger',
    ) as HTMLButtonElement;
    expect(account.getAttribute('aria-expanded')).toBe('false');
    account.click();
    fixture.detectChanges();
    expect(account.getAttribute('aria-expanded')).toBe('true');
    expect(fixture.nativeElement.textContent).toContain('Preferências');
    fixture.componentInstance.logout();
    expect(auth.logout).toHaveBeenCalledOnce();
    expect(navigate).toHaveBeenCalledWith(['/campeonatos']);
  });

  it('fecha o menu da conta com Escape e devolve o foco ao acionador', async () => {
    authenticated.set(true);
    user.set({ ...user(), authenticated: true, displayName: 'Mateus' });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const account = fixture.nativeElement.querySelector(
      '.account-menu__trigger',
    ) as HTMLButtonElement;
    account.click();
    fixture.detectChanges();
    fixture.componentInstance.onEscape();
    fixture.detectChanges();
    await new Promise((resolve) => setTimeout(resolve));
    expect(fixture.componentInstance.userMenuOpen()).toBe(false);
    expect(document.activeElement).toBe(account);
  });
});
