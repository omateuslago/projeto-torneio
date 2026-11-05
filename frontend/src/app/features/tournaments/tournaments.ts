import {
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Tournament, TournamentFormat, statusLabels, formatLabels } from '../../core/models';
import { apiError } from '../../core/api-error';
import { trimmedRequired } from '../../shared/trimmed-required';

@Component({
  selector: 'app-tournaments',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './tournaments.html',
})
export class Tournaments {
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly tournaments = signal<Tournament[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly statusLabels = statusLabels;
  readonly formatLabels = formatLabels;
  readonly isCreateModalOpen = signal(false);
  private readonly nameInput = viewChild<ElementRef<HTMLInputElement>>('nameInput');
  private readonly dialog = viewChild<ElementRef<HTMLElement>>('dialog');
  private opener: HTMLElement | null = null;
  openCreateModal(event?: Event) {
    this.opener = (event?.currentTarget as HTMLElement) ?? null;
    this.saveError.set('');
    this.isCreateModalOpen.set(true);
  }
  closeCreateModal() {
    if (this.saving()) return;
    this.isCreateModalOpen.set(false);
    this.opener?.focus();
  }
  onDialogKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeCreateModal();
    }
    if (event.key !== 'Tab') return;
    const items = this.dialog()?.nativeElement.querySelectorAll<HTMLElement>(
      'button:not(:disabled), input:not(:disabled), select:not(:disabled)',
    );
    if (!items?.length) return;
    const first = items[0],
      last = items[items.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    }
    if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
    sport: ['', [trimmedRequired, Validators.maxLength(80)]],
    maxTeams: [
      8,
      [Validators.required, Validators.min(2), Validators.max(128), Validators.pattern(/^\d+$/)],
    ],
    format: ['RoundRobin' as TournamentFormat, Validators.required],
  });

  constructor() {
    this.load();
    afterRenderEffect(() => {
      if (this.isCreateModalOpen()) this.nameInput()?.nativeElement.focus();
    });
  }

  load() {
    this.loading.set(true);
    this.loadError.set('');
    this.api
      .listTournaments()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (tournaments) => this.tournaments.set(tournaments),
        error: (error) => this.loadError.set(apiError(error)),
      });
  }

  create() {
    if (this.saving()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;
    this.saving.set(true);
    this.saveError.set('');
    const value = this.form.getRawValue();
    this.api
      .createTournament({ ...value, name: value.name.trim(), sport: value.sport.trim() })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: (tournament) => {
          this.tournaments.update((rows) => [...rows, tournament]);
          this.saving.set(false);
          this.closeCreateModal();
          this.form.reset();
        },
        error: (error) => this.saveError.set(apiError(error)),
      });
  }
}
