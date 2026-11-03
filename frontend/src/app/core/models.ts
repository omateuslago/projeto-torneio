export type TournamentFormat = 'SingleElimination' | 'RoundRobin';
export type TournamentStatus = 'Draft' | 'Open' | 'Closed' | 'InProgress' | 'Finished';
export interface Tournament {
  id: string;
  name: string;
  sport: string;
  maxTeams: number;
  format: TournamentFormat;
  status: TournamentStatus;
}
export type CreateTournament = Pick<Tournament, 'name' | 'sport' | 'maxTeams' | 'format'>;
export interface Team {
  id: string;
  name: string;
}
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
  InProgress: 'Em andamento',
  Finished: 'Finalizado',
  Draft: 'Rascunho',
  Open: 'Inscrições abertas',
  Closed: 'Inscrições encerradas',
};
export const formatLabels: Record<TournamentFormat, string> = {
  SingleElimination: 'Mata-mata simples',
  RoundRobin: 'Pontos corridos',
};

export interface Match {
  id: string;
  tournamentId: string;
  homeTeamId: string;
  homeTeamName: string;
  awayTeamId: string;
  awayTeamName: string;
  status: 'Pending' | 'Finished' | 'AwaitingConfirmation' | 'Disputed';
  homeScore: number | null;
  awayScore: number | null;
  createdAt: string;
  finishedAt: string | null;
  resultSource?: 'Manual' | 'Bilateral' | 'DisputeResolution' | null;
  submissions?: ScoreSubmission[];
  dispute?: Dispute | null;
}
export interface ScoreSubmission {
  id: string;
  matchId: string;
  submittedByTeamId: string;
  homeScore: number;
  awayScore: number;
  submittedAt: string;
  updatedAt: string | null;
}
export interface Dispute {
  id: string;
  matchId: string;
  status: 'Open' | 'Resolved';
  openedAt: string;
  resolvedAt: string | null;
  resolutionNote: string | null;
}
export interface Standing {
  teamId: string;
  teamName: string;
  played: number;
  wins: number;
  draws: number;
  losses: number;
  goalsFor: number;
  goalsAgainst: number;
  goalDifference: number;
  points: number;
}
