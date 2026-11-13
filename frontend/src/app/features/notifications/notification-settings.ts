import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { NotificationApi } from '../../core/notification-api';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-notification-settings',
  imports: [ReactiveFormsModule],
  templateUrl: './notification-settings.html',
})
export class NotificationSettings {
  private readonly api = inject(NotificationApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly message = signal('');
  readonly form = inject(FormBuilder).nonNullable.group({
    whatsAppPhoneNumber: ['', [Validators.pattern(/^\+[1-9]\d{7,14}$/)]],
    whatsAppEnabled: [false],
  });

  constructor() {
    this.api
      .preferences()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (preference) =>
          this.form.setValue({
            whatsAppPhoneNumber: preference.whatsAppPhoneNumber ?? '',
            whatsAppEnabled: preference.whatsAppEnabled,
          }),
        error: (error) => this.error.set(apiError(error)),
      });
  }

  save() {
    this.form.markAllAsTouched();
    const value = this.form.getRawValue();
    if (
      this.form.invalid ||
      (value.whatsAppEnabled && !value.whatsAppPhoneNumber) ||
      this.saving()
    ) {
      this.error.set('Informe um número internacional válido antes de habilitar o WhatsApp.');
      return;
    }
    this.saving.set(true);
    this.error.set('');
    this.message.set('');
    this.api
      .savePreferences({
        whatsAppPhoneNumber: value.whatsAppPhoneNumber || null,
        whatsAppEnabled: value.whatsAppEnabled,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (preference) => {
          this.form.setValue({
            whatsAppPhoneNumber: preference.whatsAppPhoneNumber ?? '',
            whatsAppEnabled: preference.whatsAppEnabled,
          });
          this.message.set('Preferências salvas.');
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
