import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AccessApi } from '../../core/access-api';
import { AuthService } from '../../core/auth.service';
import { AcceptInvitation } from './accept-invitation';

describe('AcceptInvitation', () => {
  const authenticated = signal(false);
  const auth = { isAuthenticated: authenticated, refresh: vi.fn() };
  const api = { preview: vi.fn(), accept: vi.fn() };
  const router = { navigate: vi.fn() };

  beforeEach(() => {
    authenticated.set(false);
    auth.refresh.mockReset();
    api.preview.mockReset();
    api.accept.mockReset();
    router.navigate.mockReset();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: AccessApi, useValue: api },
        { provide: Router, useValue: router },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ token: 'secure-token' }) } },
        },
      ],
    });
  });

  it('oferece login e cadastro com returnUrl para usuário anônimo', () => {
    const fixture = TestBed.createComponent(AcceptInvitation);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Entre ou crie uma conta');
    expect(api.preview).not.toHaveBeenCalled();
    expect(fixture.componentInstance.returnUrl).toContain('secure-token');
  });

  it('carrega preview, aceita, atualiza a sessão e navega para o campeonato', () => {
    authenticated.set(true);
    api.preview.mockReturnValue(
      of({
        type: 'TournamentOrganizer',
        targetName: 'Copa Brakvyo',
        targetId: 'cup',
        expiresAt: '2026-11-14T00:00:00Z',
        status: 'Pending',
      }),
    );
    api.accept.mockReturnValue(of({ type: 'TournamentOrganizer', targetId: 'cup' }));
    auth.refresh.mockReturnValue(of({}));
    const fixture = TestBed.createComponent(AcceptInvitation);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Copa Brakvyo');
    fixture.componentInstance.accept();
    expect(auth.refresh).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/campeonatos', 'cup']);
  });
});
