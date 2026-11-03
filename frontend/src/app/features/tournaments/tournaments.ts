import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Tournament, TournamentFormat, statusLabels } from '../../core/models';
import { apiError } from '../../core/api-error';
import { trimmedRequired } from '../../shared/trimmed-required';

@Component({
  selector: 'app-tournaments',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './tournaments.html',
})
export class Tournaments {
  private readonly api = inject(TournamentApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly tournaments = signal<Tournament[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly statusLabels = statusLabels;
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
    sport: ['', [trimmedRequired, Validators.maxLength(80)]],
    maxTeams: [8, [Validators.required, Validators.min(2), Validators.max(128), Validators.pattern(/^\d+$/)]],
    format: ['RoundRobin' as TournamentFormat, Validators.required],
  });

  constructor() { this.load(); }

  load() {
    this.loading.set(true);
    this.loadError.set('');
    this.api.listTournaments().pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false))).subscribe({
      next: tournaments => this.tournaments.set(tournaments),
      error: error => this.loadError.set(apiError(error)),
    });
  }

  create() {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.saving.set(true);
    this.saveError.set('');
    const value = this.form.getRawValue();
    this.api.createTournament({ ...value, name: value.name.trim(), sport: value.sport.trim() })
      .pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
        next: tournament => void this.router.navigate(['/campeonatos', tournament.id]),
        error: error => this.saveError.set(apiError(error)),
      });
  }
}
