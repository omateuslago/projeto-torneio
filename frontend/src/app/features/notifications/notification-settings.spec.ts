import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { NotificationApi } from '../../core/notification-api';
import { NotificationSettings } from './notification-settings';

describe('NotificationSettings', () => {
  const api = { preferences: vi.fn(), savePreferences: vi.fn() };

  beforeEach(() => {
    api.preferences.mockReset();
    api.savePreferences.mockReset();
    api.preferences.mockReturnValue(
      of({ whatsAppPhoneNumber: '+5511999999999', whatsAppEnabled: true }),
    );
    TestBed.configureTestingModule({ providers: [{ provide: NotificationApi, useValue: api }] });
  });

  it('carrega as preferências atuais', () => {
    const fixture = TestBed.createComponent(NotificationSettings);
    fixture.detectChanges();
    expect(fixture.componentInstance.form.getRawValue()).toEqual({
      whatsAppPhoneNumber: '+5511999999999',
      whatsAppEnabled: true,
    });
  });

  it('salva telefone e permite habilitar ou desabilitar WhatsApp', () => {
    api.savePreferences.mockReturnValue(
      of({ whatsAppPhoneNumber: '+5511888888888', whatsAppEnabled: false }),
    );
    const fixture = TestBed.createComponent(NotificationSettings);
    fixture.componentInstance.form.setValue({
      whatsAppPhoneNumber: '+5511888888888',
      whatsAppEnabled: false,
    });
    fixture.componentInstance.save();
    expect(api.savePreferences).toHaveBeenCalledWith({
      whatsAppPhoneNumber: '+5511888888888',
      whatsAppEnabled: false,
    });
  });

  it('rejeita telefone inválido ao habilitar', () => {
    const fixture = TestBed.createComponent(NotificationSettings);
    fixture.componentInstance.form.setValue({
      whatsAppPhoneNumber: '119999',
      whatsAppEnabled: true,
    });
    fixture.componentInstance.save();
    fixture.detectChanges();
    expect(api.savePreferences).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('número internacional válido');
  });
});
