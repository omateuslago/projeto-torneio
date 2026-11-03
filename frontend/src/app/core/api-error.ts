import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblem } from './models';

const messages: Record<string, string> = {
  'Team is already registered.': 'Esta equipe já está inscrita neste campeonato.',
  'Tournament is full.': 'O campeonato está lotado. Todas as vagas foram preenchidas.',
  'Registration is not open.': 'As inscrições deste campeonato estão fechadas.',
  'Registration or tournament changed concurrently. Reload and retry.': 'As inscrições foram atualizadas por outra solicitação. Atualize a página e tente novamente.',
  'Allowed transitions: Draft → Open → Closed.': 'Essa mudança de estado não é permitida. Atualize o campeonato e tente novamente.',
};

export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Não foi possível concluir a operação. Tente novamente.';
  if (error.status === 0) return 'Não foi possível conectar ao servidor. Verifique sua conexão e tente novamente.';
  if (error.status === 404) return 'O registro solicitado não foi encontrado.';
  const problem: ApiProblem | null = error.error && typeof error.error === 'object' ? error.error : null;
  if (problem?.detail) return messages[problem.detail] ?? problem.detail;
  if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
  return 'Não foi possível concluir a operação. Tente novamente.';
}
