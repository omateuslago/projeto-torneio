import { signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { AccessApi } from '../../core/access-api';
import { AuthService } from '../../core/auth.service';
import { AccessManagement } from './access-management';

describe('AccessManagement', () => {
  const api = { members: vi.fn(), invite: vi.fn(), revoke: vi.fn(), remove: vi.fn() };
  const auth = { user: signal({ id: 'owner' }) };

  beforeEach(() => {
    Object.values(api).forEach((mock) => mock.mockReset());
    TestBed.configureTestingModule({
      providers: [
        { provide: AccessApi, useValue: api },
        { provide: AuthService, useValue: auth },
      ],
    });
  });

  it('lista membros e cria convite de organizer', () => {
    api.members.mockReturnValue(
      of({
        members: [{ userId: 'owner', displayName: 'Mateus', email: 'mateus@example.com' }],
        pendingInvitations: [],
      }),
    );
    api.invite.mockReturnValue(
      of({
        id: 'invite',
        token: 'token',
        expiresAt: '2026-11-14T00:00:00Z',
        relativePath: '/convites/aceitar?token=token',
      }),
    );
    const fixture = TestBed.createComponent(AccessManagement);
    fixture.componentRef.setInput('kind', 'tournament');
    fixture.componentRef.setInput('targetId', 'cup');
    fixture.componentRef.setInput('title', 'Organizadores');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Mateus');
    fixture.componentInstance.form.setValue({ email: 'new@example.com' });
    fixture.componentInstance.invite();
    expect(api.invite).toHaveBeenCalledWith('tournament', 'cup', 'new@example.com');
    expect(fixture.componentInstance.invitationLink()).toContain('/convites/aceitar?token=token');
  });

  it('exibe 403 sem alterar a sessão', () => {
    api.members.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 403 })));
    const fixture = TestBed.createComponent(AccessManagement);
    fixture.componentRef.setInput('kind', 'team');
    fixture.componentRef.setInput('targetId', 'team');
    fixture.componentRef.setInput('title', 'Managers');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Você não tem permissão');
    expect(auth.user().id).toBe('owner');
  });
});
