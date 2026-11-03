import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, forkJoin, Subject, switchMap, catchError, EMPTY } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Registration, Team, Tournament } from '../../core/models';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-registrations',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './registrations.html',
})
export class Registrations {
  readonly tournament = input.required<Tournament>();
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload = new Subject<string>();
  readonly registrations = signal<Registration[]>([]);
  readonly teams = signal<Team[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly message = signal('');
  readonly form = inject(FormBuilder).nonNullable.group({ teamId: ['', Validators.required] });

  constructor() {
    this.reload
      .pipe(
        switchMap((id) => {
          this.loading.set(true);
          this.loadError.set('');
          return forkJoin({
            registrations: this.api.listRegistrations(id),
            teams: this.api.listTeams(),
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
        this.registrations.set(data.registrations);
        this.teams.set(data.teams);
      });
    effect(() => {
      this.reload.next(this.tournament().id);
    });
  }
  load() {
    this.reload.next(this.tournament().id);
  }
  register() {
    if (this.saving() || this.tournament().status !== 'Open') return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.saving.set(true);
    this.saveError.set('');
    this.message.set('');
    this.api
      .registerTeam(this.tournament().id, this.form.getRawValue().teamId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (registration) => {
          this.form.reset();
          this.message.set(`Equipe ${registration.teamName} inscrita.`);
          this.load();
        },
        error: (error) => {
          this.saveError.set(apiError(error));
          this.load();
        },
      });
  }
}
