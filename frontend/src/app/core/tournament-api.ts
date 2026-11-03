import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environment';
import {
  CreateTournament,
  Match,
  Standing,
  Registration,
  Team,
  Tournament,
  TournamentStatus,
  ScoreSubmission,
  Dispute,
} from './models';

@Injectable({ providedIn: 'root' })
export class TournamentApi {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  listTournaments() {
    return this.http.get<Tournament[]>(`${this.base}/tournaments`);
  }
  getTournament(id: string) {
    return this.http.get<Tournament>(`${this.base}/tournaments/${id}`);
  }
  createTournament(request: CreateTournament) {
    return this.http.post<Tournament>(`${this.base}/tournaments`, request);
  }
  changeStatus(id: string, status: TournamentStatus) {
    return this.http.put<Tournament>(`${this.base}/tournaments/${id}/status`, { status });
  }
  listTeams() {
    return this.http.get<Team[]>(`${this.base}/teams`);
  }
  createTeam(name: string) {
    return this.http.post<Team>(`${this.base}/teams`, { name });
  }
  listRegistrations(id: string) {
    return this.http.get<Registration[]>(`${this.base}/tournaments/${id}/registrations`);
  }
  registerTeam(tournamentId: string, teamId: string) {
    return this.http.post<Registration>(`${this.base}/tournaments/${tournamentId}/registrations`, {
      teamId,
    });
  }

  listMatches(id: string) {
    return this.http.get<Match[]>(`${this.base}/tournaments/${id}/matches`);
  }
  generateMatches(id: string) {
    return this.http.post<Match[]>(`${this.base}/tournaments/${id}/matches/generate`, {});
  }
  recordResult(id: string, homeScore: number, awayScore: number) {
    return this.http.put<Match>(`${this.base}/matches/${id}/result`, { homeScore, awayScore });
  }
  standings(id: string) {
    return this.http.get<Standing[]>(`${this.base}/tournaments/${id}/standings`);
  }
  submitScore(matchId: string, teamId: string, homeScore: number, awayScore: number) {
    return this.http.put<ScoreSubmission>(
      `${this.base}/matches/${matchId}/score-submissions/${teamId}`,
      { homeScore, awayScore },
    );
  }
  resolveDispute(id: string, homeScore: number, awayScore: number, resolutionNote: string) {
    return this.http.put<Dispute>(`${this.base}/disputes/${id}/resolve`, {
      homeScore,
      awayScore,
      resolutionNote,
    });
  }
}
