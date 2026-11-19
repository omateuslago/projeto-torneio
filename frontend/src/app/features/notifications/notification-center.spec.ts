import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { NotificationApi } from '../../core/notification-api';
import { NotificationCenter } from './notification-center';

describe('NotificationCenter', () => {
  const api = { notifications: vi.fn() };

  beforeEach(() => {
    api.notifications.mockReset();
    TestBed.configureTestingModule({ providers: [{ provide: NotificationApi, useValue: api }] });
  });

  it('lista notificações e traduz status do WhatsApp', () => {
    api.notifications.mockReturnValue(
      of([
        {
          id: 'notification',
          type: 'MatchScheduled',
          title: 'Partida agendada',
          summary: 'Azul × Verde',
          createdAt: '2026-11-13T19:30:00Z',
          whatsAppStatus: 'Sent',
        },
      ]),
    );
    const fixture = TestBed.createComponent(NotificationCenter);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Partida agendada');
    expect(fixture.nativeElement.textContent).toContain('WhatsApp: Enviado');
  });

  it('mostra falha da API e permite atualizar', () => {
    api.notifications.mockReturnValue(
      throwError(() => new HttpErrorResponse({ status: 500, statusText: 'Server Error' })),
    );
    const fixture = TestBed.createComponent(NotificationCenter);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Não foi possível concluir');
    api.notifications.mockReturnValue(of([]));
    fixture.componentInstance.load();
    expect(api.notifications).toHaveBeenCalledTimes(2);
  });
});
