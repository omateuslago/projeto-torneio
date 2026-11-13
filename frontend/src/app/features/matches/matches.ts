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
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EMPTY, Observable, Subject, catchError, finalize, forkJoin, switchMap } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Match, Standing, Tournament } from '../../core/models';
import { apiError } from '../../core/api-error';
import { trimmedRequired } from '../../shared/trimmed-required';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-matches',
  imports: [ReactiveFormsModule],
  templateUrl: './matches.html',
})
export class Matches {
  readonly auth = inject(AuthService);
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
  readonly mode = signal<'submission' | 'resolution' | 'manual'>('manual');
  readonly submittingTeam = signal('');
  readonly resolutionNote = new FormControl('', {
    nonNullable: true,
    validators: [trimmedRequired, Validators.maxLength(2000)],
  });
  readonly pending = computed(() =>
    this.matches().filter(
      (m) => m.status === 'AwaitingConfirmation' || m.dispute?.status === 'Open',
    ),
  );
  submission(match: Match, teamId: string) {
    return match.submissions?.find((s) => s.submittedByTeamId === teamId);
  }
  waitingFor(match: Match) {
    return this.submission(match, match.homeTeamId) ? match.awayTeamName : match.homeTeamName;
  }
  manageableTeams(match: Match) {
    return [
      { id: match.homeTeamId, name: match.homeTeamName },
      { id: match.awayTeamId, name: match.awayTeamName },
    ].filter((team) => this.auth.canManageTeam(team.id));
  }
  matchLabel(match: Match) {
    if (match.status === 'AwaitingConfirmation') return 'Aguardando confirmação';
    if (match.status === 'Disputed') return 'Resultado contestado';
    if (match.status === 'Pending') return 'Pendente';
    return match.resultSource === 'Bilateral'
      ? 'Resultado confirmado pelas equipes'
      : match.resultSource === 'DisputeResolution'
        ? 'Disputa resolvida'
        : 'Resultado administrativo';
  }
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
    this.mode.set('manual');
    this.editing.set(match);
    this.actionError.set('');
    this.message.set('');
    this.form.reset({ homeScore: match.homeScore ?? 0, awayScore: match.awayScore ?? 0 });
  }
  sendAs(match: Match, teamId: string) {
    this.edit(match);
    this.mode.set('submission');
    this.submittingTeam.set(teamId);
    const previous = this.submission(match, teamId);
    this.form.reset({ homeScore: previous?.homeScore ?? 0, awayScore: previous?.awayScore ?? 0 });
  }
  resolve(match: Match) {
    this.edit(match);
    this.mode.set('resolution');
    this.resolutionNote.reset('');
  }
  saveResult() {
    const match = this.editing();
    if (!match || this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    if (this.mode() === 'resolution') {
      this.resolutionNote.markAsTouched();
      if (this.resolutionNote.invalid || !match.dispute) return;
    }
    this.saving.set(true);
    this.actionError.set('');
    this.message.set('');
    const scores = this.form.getRawValue();
    const request: Observable<unknown> =
      this.mode() === 'submission'
        ? this.api.submitScore(match.id, this.submittingTeam(), scores.homeScore, scores.awayScore)
        : this.mode() === 'resolution'
          ? this.api.resolveDispute(
              match.dispute!.id,
              scores.homeScore,
              scores.awayScore,
              this.resolutionNote.value.trim(),
            )
          : this.api.recordResult(match.id, scores.homeScore, scores.awayScore);
    request
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: () => {
          this.editing.set(null);
          this.message.set(
            this.mode() === 'submission'
              ? 'Envio salvo. Confira as confirmações abaixo.'
              : 'Resultado salvo. Classificação atualizada.',
          );
          this.load();
        },
        error: (error) => this.actionError.set(apiError(error)),
      });
  }
}
