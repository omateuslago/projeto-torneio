import {
  Component,
  DestroyRef,
  ElementRef,
  afterRenderEffect,
  Renderer2,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize } from 'rxjs';
import { TournamentApi } from '../../core/tournament-api';
import { Tournament, TournamentFormat, formatLabels, statusLabels } from '../../core/models';
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
  private readonly renderer = inject(Renderer2);
  private readonly document = inject(DOCUMENT);
  private lastTrigger?: HTMLElement;
  @ViewChild('createButton') private createButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('dialog') private dialog?: ElementRef<HTMLElement>;
  @ViewChild('nameInput') private nameInput?: ElementRef<HTMLInputElement>;
  readonly tournaments = signal<Tournament[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly isCreateModalOpen = signal(false);
  readonly statusLabels = statusLabels;
  readonly formatLabels = formatLabels;
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
      if (this.isCreateModalOpen()) this.nameInput?.nativeElement.focus();
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

  openCreateModal(event?: Event) {
    if (event?.currentTarget instanceof HTMLElement) {
      this.lastTrigger = event.currentTarget;
    }
    this.isCreateModalOpen.set(true);
    this.saveError.set('');
    this.renderer.setStyle(this.document.body, 'overflow', 'hidden');
  }

  closeCreateModal(force = false) {
    if (this.saving() && !force) return;
    this.isCreateModalOpen.set(false);
    this.saveError.set('');
    this.renderer.removeStyle(this.document.body, 'overflow');
    setTimeout(() => (this.lastTrigger ?? this.createButton?.nativeElement)?.focus());
  }

  onDialogKeydown(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.closeCreateModal();
      return;
    }
    if (event.key !== 'Tab' || !this.dialog) return;
    const focusable = Array.from(
      this.dialog.nativeElement.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), select:not([disabled])',
      ),
    );
    if (!focusable.length) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && this.document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && this.document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
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
          this.tournaments.update((current) => [...current, tournament]);
          this.form.reset({
            name: '',
            sport: '',
            maxTeams: 8,
            format: 'RoundRobin',
          });
          this.closeCreateModal(true);
        },
        error: (error) => this.saveError.set(apiError(error)),
      });
  }

  ngOnDestroy() {
    this.renderer.removeStyle(this.document.body, 'overflow');
  }
}
