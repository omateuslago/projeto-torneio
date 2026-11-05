import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
export interface Venue {
  id: string;
  name: string;
  address: string;
  active: boolean;
}
export interface Area {
  id: string;
  venueId: string;
  name: string;
  active: boolean;
}
export interface Referee {
  id: string;
  name: string;
  active: boolean;
}
export interface Availability {
  id: string;
  startsAt: string;
  endsAt: string;
}
export interface Settings {
  matchDurationMinutes: number;
  timeZoneId: string;
}
export interface ScheduledMatch {
  matchId: string;
  homeTeamId: string;
  homeTeam: string;
  awayTeamId: string;
  awayTeam: string;
  startsAt: string | null;
  endsAt: string | null;
  playingAreaId: string | null;
  playingArea: string | null;
  venue: string | null;
  refereeId: string | null;
  referee: string | null;
  matchStatus: string;
}
export type ResourceKind = 'teams' | 'playing-areas' | 'referees';
@Injectable({ providedIn: 'root' })
export class SchedulingApi {
  private http = inject(HttpClient);
  private base = environment.apiUrl;
  private tournament(id: string) {
    return `${this.base}/tournaments/${id}`;
  }
  settings(t: string) {
    return this.http.get<Settings>(`${this.tournament(t)}/schedule-settings`);
  }
  saveSettings(t: string, data: Settings) {
    return this.http.put<Settings>(`${this.tournament(t)}/schedule-settings`, data);
  }
  venues(t: string) {
    return this.http.get<Venue[]>(`${this.tournament(t)}/venues`);
  }
  createVenue(t: string, data: { name: string; address: string }) {
    return this.http.post<Venue>(`${this.tournament(t)}/venues`, data);
  }
  areas(t: string) {
    return this.http.get<Area[]>(`${this.tournament(t)}/playing-areas`);
  }
  createArea(t: string, venue: string, name: string) {
    return this.http.post<Area>(`${this.tournament(t)}/venues/${venue}/playing-areas`, { name });
  }
  referees(t: string) {
    return this.http.get<Referee[]>(`${this.tournament(t)}/referees`);
  }
  createReferee(t: string, name: string) {
    return this.http.post<Referee>(`${this.tournament(t)}/referees`, { name });
  }
  schedule(t: string) {
    return this.http.get<ScheduledMatch[]>(`${this.tournament(t)}/schedule`);
  }
  saveSchedule(
    id: string,
    data: { startsAt: string; playingAreaId: string; refereeId: string | null },
  ) {
    return this.http.put<ScheduledMatch>(`${this.base}/matches/${id}/schedule`, data);
  }
  availability(t: string, kind: ResourceKind, id: string) {
    return this.http.get<Availability[]>(`${this.tournament(t)}/${kind}/${id}/availability`);
  }
  saveAvailability(
    t: string,
    kind: ResourceKind,
    resource: string,
    id: string | null,
    data: { startsAt: string; endsAt: string },
  ) {
    const url = `${this.tournament(t)}/${kind}/${resource}/availability`;
    return id
      ? this.http.put<Availability>(`${url}/${id}`, data)
      : this.http.post<Availability>(url, data);
  }
  deleteAvailability(t: string, kind: ResourceKind, resource: string, id: string) {
    return this.http.delete<void>(`${this.tournament(t)}/${kind}/${resource}/availability/${id}`);
  }
}
