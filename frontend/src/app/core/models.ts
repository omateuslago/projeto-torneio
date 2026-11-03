export type TournamentFormat = 'SingleElimination' | 'RoundRobin';
export type TournamentStatus = 'Draft' | 'Open' | 'Closed';
export interface Tournament {
  id: string;
  name: string;
  sport: string;
  maxTeams: number;
  format: TournamentFormat;
  status: TournamentStatus;
}
export type CreateTournament = Pick<Tournament, 'name' | 'sport' | 'maxTeams' | 'format'>;
export interface Team { id: string; name: string; }
export interface Registration {
  id: string;
  tournamentId: string;
  teamId: string;
  teamName: string;
  registeredAt: string;
}
export interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}
export const statusLabels: Record<TournamentStatus, string> = {
  Draft: 'Rascunho', Open: 'Inscrições abertas', Closed: 'Inscrições encerradas',
};
export const formatLabels: Record<TournamentFormat, string> = {
  SingleElimination: 'Mata-mata simples', RoundRobin: 'Pontos corridos',
};
