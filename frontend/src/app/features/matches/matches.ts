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
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Subject, catchError, finalize, forkJoin, switchMap } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Match, Standing, Tournament } from '../../core/models';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-matches',
  imports: [ReactiveFormsModule],
  templateUrl: './matches.html',
})
export class Matches {
  readonly tournament = input.required<Tournament>();
  readonly tournamentChanged = output<Tournament>();
  private readonly tournamentId = computed(() => this.tournament().id);
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<string>();
  readonly matches = signal<Match[]>([]);
  readonly standings = signal<Standing[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly actionError = signal('');
  readonly message = signal('');
  readonly editing = signal<Match | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    homeScore: [
      0,
      [
        Validators.required,
        Validators.min(0),
        Validators.max(2147483647),
        Validators.pattern(/^\d+$/),
      ],
    ],
    awayScore: [
      0,
      [
        Validators.required,
        Validators.min(0),
        Validators.max(2147483647),
        Validators.pattern(/^\d+$/),
      ],
    ],
  });

  constructor() {
    this.reload
      .pipe(
        switchMap((id) => {
          this.loading.set(true);
          this.loadError.set('');
          return forkJoin({
            matches: this.api.listMatches(id),
            standings: this.api.standings(id),
            tournament: this.api.getTournament(id),
          }).pipe(
            catchError((error) => {
              this.loadError.set(apiError(error));
              return EMPTY;
            }),
            finalize(() => this.loading.set(false)),
          );
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((data) => {
        this.matches.set(data.matches);
        this.standings.set(data.standings);
        this.tournamentChanged.emit(data.tournament);
      });
    effect(() => {
      this.editing.set(null);
      this.load(this.tournamentId());
    });
  }
  load(id = this.tournamentId()) {
    this.reload.next(id);
  }
  generate() {
    if (this.saving()) return;
    this.saving.set(true);
    this.actionError.set('');
    this.message.set('');
    this.api
      .generateMatches(this.tournamentId())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.message.set('Confrontos gerados. O campeonato está em andamento.');
          this.load();
        },
        error: (error) => this.actionError.set(apiError(error)),
      });
  }
  edit(match: Match) {
    this.editing.set(match);
    this.actionError.set('');
    this.message.set('');
    this.form.reset({ homeScore: match.homeScore ?? 0, awayScore: match.awayScore ?? 0 });
  }
  saveResult() {
    const match = this.editing();
    if (!match || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.saving.set(true);
    this.actionError.set('');
    this.message.set('');
    const scores = this.form.getRawValue();
    this.api
      .recordResult(match.id, scores.homeScore, scores.awayScore)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.editing.set(null);
          this.message.set('Resultado salvo. Classificação atualizada.');
          this.load();
        },
        error: (error) => this.actionError.set(apiError(error)),
      });
  }
}
