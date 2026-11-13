import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface NotificationPreference {
  whatsAppPhoneNumber: string | null;
  whatsAppEnabled: boolean;
}

export interface UserNotification {
  id: string;
  type: string;
  title: string;
  summary: string;
  createdAt: string;
  whatsAppStatus: 'Pending' | 'Processing' | 'Sent' | 'Failed' | 'Skipped';
}

@Injectable({ providedIn: 'root' })
export class NotificationApi {
  private readonly http = inject(HttpClient);

  preferences() {
    return this.http.get<NotificationPreference>('/api/me/notification-preferences');
  }
  savePreferences(preference: NotificationPreference) {
    return this.http.put<NotificationPreference>('/api/me/notification-preferences', preference);
  }
  notifications() {
    return this.http.get<UserNotification[]>('/api/me/notifications');
  }
}
