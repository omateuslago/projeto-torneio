import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of, switchMap, tap } from 'rxjs';

export interface CurrentUser {
  authenticated: boolean;
  id: string | null;
  email: string | null;
  displayName: string | null;
  isPlatformAdmin: boolean;
  organizerTournamentIds: string[];
  managedTeamIds: string[];
}

const anonymous: CurrentUser = {
  authenticated: false,
  id: null,
  email: null,
  displayName: null,
  isPlatformAdmin: false,
  organizerTournamentIds: [],
  managedTeamIds: [],
};

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  readonly user = signal<CurrentUser>(anonymous);
  readonly loaded = signal(false);
  readonly isAuthenticated = computed(() => this.user().authenticated);
  readonly isPlatformAdmin = computed(() => this.user().isPlatformAdmin);

  refresh(): Observable<CurrentUser> {
    return this.csrf().pipe(
      switchMap(() => this.http.get<CurrentUser>('/api/auth/me')),
      catchError(() => of(anonymous)),
      tap((user) => {
        this.user.set(user);
        this.loaded.set(true);
      }),
    );
  }
  login(email: string, password: string, rememberMe: boolean) {
    return this.csrf().pipe(
      switchMap(() =>
        this.http.post<CurrentUser>('/api/auth/login', { email, password, rememberMe }),
      ),
      switchMap((user) => this.csrf().pipe(map(() => user))),
      tap((user) => this.user.set(user)),
    );
  }
  register(email: string, password: string, displayName: string) {
    return this.csrf().pipe(
      switchMap(() =>
        this.http.post<CurrentUser>('/api/auth/register', { email, password, displayName }),
      ),
      switchMap((user) => this.csrf().pipe(map(() => user))),
      tap((user) => this.user.set(user)),
    );
  }
  logout() {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      tap(() => this.user.set(anonymous)),
      map(() => undefined),
    );
  }
  canManageTournament(id: string) {
    const user = this.user();
    return user.isPlatformAdmin || user.organizerTournamentIds.includes(id);
  }
  canManageTeam(id: string) {
    const user = this.user();
    return user.isPlatformAdmin || user.managedTeamIds.includes(id);
  }
  recordCreatedTournament(id: string) {
    const user = this.user();
    if (user.authenticated && !user.organizerTournamentIds.includes(id))
      this.user.set({ ...user, organizerTournamentIds: [...user.organizerTournamentIds, id] });
  }
  recordCreatedTeam(id: string) {
    const user = this.user();
    if (user.authenticated && !user.managedTeamIds.includes(id))
      this.user.set({ ...user, managedTeamIds: [...user.managedTeamIds, id] });
  }
  private csrf() {
    return this.http.get<{ token: string }>('/api/auth/csrf');
  }
}
