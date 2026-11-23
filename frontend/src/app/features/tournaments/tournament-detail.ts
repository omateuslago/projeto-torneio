import { Component, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { catchError, finalize, of, switchMap, tap } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Tournament, TournamentStatus, formatLabels, statusLabels } from '../../core/models';
import { Matches } from '../matches/matches';
import { Registrations } from '../registrations/registrations';
import { apiError } from '../../core/api-error';
import { AuthService } from '../../core/auth.service';
import { AccessManagement } from '../access/access-management';
import { ConfirmDialog } from '../../shared/confirm-dialog';
import { Bracket } from '../bracket/bracket';

@Component({
  selector: 'app-tournament-detail',
  imports: [RouterLink, Registrations, Matches, Bracket, AccessManagement, ConfirmDialog],
  templateUrl: './tournament-detail.html',
  styleUrl: './tournament-detail.css',
})
export class TournamentDetail {
  readonly auth = inject(AuthService);
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly tournament = signal<Tournament | null>(null);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly actionError = signal('');
  readonly message = signal('');
  readonly confirmClose = signal(false);
  readonly statusLabels = statusLabels;
  readonly formatLabels = formatLabels;
  readonly bracket = viewChild(Bracket);

  constructor() {
    inject(ActivatedRoute)
      .paramMap.pipe(
        tap(() => {
          this.loading.set(true);
          this.loadError.set('');
          this.actionError.set('');
          this.message.set('');
          this.tournament.set(null);
        }),
        switchMap((params) =>
          this.api.getTournament(params.get('id')!).pipe(
            catchError((error) => {
              this.loadError.set(apiError(error));
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((tournament) => {
        this.tournament.set(tournament);
        this.loading.set(false);
      });
  }

  changeStatus(status: TournamentStatus) {
    const tournament = this.tournament();
    if (!tournament || this.saving()) return;
    this.saving.set(true);
    this.actionError.set('');
    this.message.set('');
    this.api
      .changeStatus(tournament.id, status)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (updated) => {
          if (this.tournament()?.id !== updated.id) return;
          this.updateTournament(updated);
          this.message.set(status === 'Open' ? 'Inscrições abertas.' : 'Inscrições encerradas.');
        },
        error: (error) => this.actionError.set(apiError(error)),
      });
  }

  updateTournament(updated: Tournament) {
    const current = this.tournament();
    if (!current || current.id !== updated.id) return;
    // Lifecycle transitions only move forward. A delayed child refresh must not
    // undo a more recent status mutation in the parent.
    const order: TournamentStatus[] = ['Draft', 'Open', 'Closed', 'InProgress', 'Finished'];
    if (order.indexOf(updated.status) < order.indexOf(current.status)) return;
    this.tournament.set(updated);
  }

  generateBracket() {
    this.bracket()?.generate();
  }
}
