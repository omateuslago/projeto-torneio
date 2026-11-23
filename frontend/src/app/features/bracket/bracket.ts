import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin, switchMap } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Bracket as BracketModel, BracketNode, Tournament } from '../../core/models';
import { apiError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { Matches } from '../matches/matches';

@Component({
  selector: 'app-bracket',
  imports: [Matches],
  templateUrl: './bracket.html',
  styleUrl: './bracket.css',
})
export class Bracket {
  readonly cardHeight = 118;
  private readonly nodeStep = 142;
  readonly auth = inject(AuthService);
  readonly tournament = input.required<Tournament>();
  readonly tournamentChanged = output<Tournament>();
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly tournamentId = computed(() => this.tournament().id);
  readonly data = signal<BracketModel | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly actionError = signal('');
  readonly message = signal('');

  constructor() {
    effect(() => this.load(this.tournamentId()));
  }

  load(id = this.tournamentId()) {
    this.loading.set(true);
    this.loadError.set('');
    this.api
      .bracket(id)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (bracket) => this.data.set(bracket),
        error: (error) => this.loadError.set(apiError(error)),
      });
  }

  generate() {
    if (this.saving()) return;
    this.saving.set(true);
    this.actionError.set('');
    this.message.set('');
    const id = this.tournamentId();
    this.api
      .generateMatches(id)
      .pipe(
        switchMap(() =>
          forkJoin({ bracket: this.api.bracket(id), tournament: this.api.getTournament(id) }),
        ),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: ({ bracket, tournament }) => {
          this.data.set(bracket);
          this.message.set('Chave gerada. O campeonato está em andamento.');
          this.tournamentChanged.emit(tournament);
        },
        error: (error) => this.actionError.set(apiError(error)),
      });
  }

  onTournamentChanged(tournament: Tournament) {
    this.tournamentChanged.emit(tournament);
  }

  score(node: BracketNode, side: 'top' | 'bottom') {
    if (!node.match) return '—';
    return side === 'top' ? (node.match.topScore ?? '—') : (node.match.bottomScore ?? '—');
  }

  teamState(node: BracketNode, teamId?: string) {
    if (!teamId) return '';
    return node.match?.winnerTeamId === teamId ? 'Vencedor' : '';
  }

  roundGap(round: number) {
    return 2 ** (round - 1) * this.nodeStep - this.cardHeight;
  }

  roundOffset(round: number) {
    return ((2 ** (round - 1) - 1) * this.nodeStep) / 2;
  }
}
