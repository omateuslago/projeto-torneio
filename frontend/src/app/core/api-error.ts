import { HttpErrorResponse } from '@angular/common/http';
import { ApiProblem } from './models';

const messages: Record<string, string> = {
  'Official result is already final.':
    'O resultado já foi confirmado. Somente uma correção administrativa pode alterá-lo.',
  'Dispute is already resolved.': 'Esta disputa já foi resolvida. Atualize as partidas.',
  'Only a participating team can submit a score.':
    'Somente uma das equipes desta partida pode enviar um placar.',
  'Close registration before generating matches.':
    'Encerre as inscrições antes de gerar os confrontos.',
  'At least two teams are required.':
    'Inscreva pelo menos duas equipes antes de encerrar as inscrições.',
  'Only RoundRobin is supported for match generation.':
    'A geração de confrontos está disponível apenas para pontos corridos.',
  'Scores cannot be negative.':
    'Os placares devem ser números inteiros iguais ou maiores que zero.',

  'Team is already registered.': 'Esta equipe já está inscrita neste campeonato.',
  'Tournament is full.': 'O campeonato está lotado. Todas as vagas foram preenchidas.',
  'Registration is not open.': 'As inscrições deste campeonato estão fechadas.',
  'Registration or tournament changed concurrently. Reload and retry.':
    'As inscrições foram atualizadas por outra solicitação. Atualize a página e tente novamente.',
  'Allowed transitions: Draft → Open → Closed.':
    'Essa mudança de estado não é permitida. Atualize o campeonato e tente novamente.',
  'An active invitation already exists for this email.':
    'Já existe um convite pendente para este email.',
  'User is already a tournament organizer.': 'Este usuário já é organizador do campeonato.',
  'User is already a team manager.': 'Este usuário já é manager da equipe.',
  'Invitation is no longer pending.': 'Este convite já foi usado ou revogado.',
  'Invitation has expired.': 'Este convite expirou.',
  'Invalid invitation token.': 'O link do convite é inválido ou expirou.',
  'You cannot remove yourself.': 'Você não pode remover o próprio acesso.',
  'The last tournament organizer cannot be removed.':
    'O último organizador do campeonato não pode ser removido.',
  'The last team manager cannot be removed.': 'O último manager da equipe não pode ser removido.',
};

export function apiError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse))
    return 'Não foi possível concluir a operação. Tente novamente.';
  if (error.status === 0)
    return 'Não foi possível conectar ao servidor. Verifique sua conexão e tente novamente.';
  if (error.status === 403) return 'Você não tem permissão para realizar esta ação.';
  if (error.status === 404) return 'O registro solicitado não foi encontrado.';
  const problem: ApiProblem | null =
    error.error && typeof error.error === 'object' ? error.error : null;
  if (problem?.detail) return messages[problem.detail] ?? problem.detail;
  if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
  return 'Não foi possível concluir a operação. Tente novamente.';
}
