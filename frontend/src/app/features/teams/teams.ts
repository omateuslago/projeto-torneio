import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Team } from '../../core/models';
import { apiError } from '../../core/api-error';
import { trimmedRequired } from '../../shared/trimmed-required';
import { AuthService } from '../../core/auth.service';
import { AccessManagement } from '../access/access-management';

@Component({
  selector: 'app-teams',
  imports: [ReactiveFormsModule, AccessManagement],
  templateUrl: './teams.html',
})
export class Teams {
  readonly auth = inject(AuthService);
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly teams = signal<Team[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly message = signal('');
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
  });
  constructor() {
    this.load();
  }
  load() {
    this.loading.set(true);
    this.loadError.set('');
    this.api
      .listTeams()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (teams) => this.teams.set(teams),
        error: (error) => this.loadError.set(apiError(error)),
      });
  }
  create() {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.saving.set(true);
    this.saveError.set('');
    this.message.set('');
    this.api
      .createTeam(this.form.getRawValue().name.trim())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (team) => {
          this.auth.recordCreatedTeam(team.id);
          this.form.reset();
          this.message.set(`Equipe ${team.name} cadastrada.`);
          this.load();
        },
        error: (error) => this.saveError.set(apiError(error)),
      });
  }
}
