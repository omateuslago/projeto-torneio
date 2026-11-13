import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';

export interface AccessMember {
  userId: string;
  displayName: string;
  email: string;
}
export interface PendingInvitation {
  id: string;
  email: string;
  expiresAt: string;
}
export interface AccessMembers {
  members: AccessMember[];
  pendingInvitations: PendingInvitation[];
}
export interface CreatedInvitation {
  id: string;
  token: string;
  expiresAt: string;
  relativePath: string;
}
export interface InvitationPreview {
  type: 'TournamentOrganizer' | 'TeamManager';
  targetName: string;
  targetId: string;
  expiresAt: string;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class AccessApi {
  private readonly http = inject(HttpClient);

  members(kind: 'tournament' | 'team', id: string) {
    return this.http.get<AccessMembers>(
      kind === 'tournament' ? `/api/tournaments/${id}/organizers` : `/api/teams/${id}/managers`,
    );
  }
  invite(kind: 'tournament' | 'team', id: string, email: string) {
    const path =
      kind === 'tournament'
        ? `/api/tournaments/${id}/organizer-invitations`
        : `/api/teams/${id}/manager-invitations`;
    return this.http.post<CreatedInvitation>(path, { email });
  }
  revoke(kind: 'tournament' | 'team', id: string, invitationId: string) {
    const path =
      kind === 'tournament'
        ? `/api/tournaments/${id}/organizer-invitations/${invitationId}`
        : `/api/teams/${id}/manager-invitations/${invitationId}`;
    return this.http.delete<void>(path);
  }
  remove(kind: 'tournament' | 'team', id: string, userId: string) {
    const path =
      kind === 'tournament'
        ? `/api/tournaments/${id}/organizers/${userId}`
        : `/api/teams/${id}/managers/${userId}`;
    return this.http.delete<void>(path);
  }
  preview(token: string) {
    return this.http.get<InvitationPreview>('/api/access-invitations/preview', {
      params: { token },
    });
  }
  accept(token: string) {
    return this.http.post<{ type: string; targetId: string }>('/api/access-invitations/accept', {
      token,
    });
  }
}
