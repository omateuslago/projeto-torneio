import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { NotificationApi, UserNotification } from '../../core/notification-api';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-notification-center',
  imports: [DatePipe],
  templateUrl: './notification-center.html',
})
export class NotificationCenter {
  private readonly api = inject(NotificationApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly notifications = signal<UserNotification[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly statusLabels: Record<UserNotification['whatsAppStatus'], string> = {
    Pending: 'Pendente',
    Processing: 'Pendente',
    Sent: 'Enviado',
    Failed: 'Falhou',
    Skipped: 'Não habilitado',
  };

  constructor() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');
    this.api
      .notifications()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (notifications) => this.notifications.set(notifications),
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
