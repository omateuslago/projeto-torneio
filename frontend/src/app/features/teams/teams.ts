import { DOCUMENT } from '@angular/common';
import {
  Component,
  DestroyRef,
  ElementRef,
  Renderer2,
  ViewChild,
  afterRenderEffect,
  inject,
  signal,
} from '@angular/core';
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
  styleUrl: './teams.css',
})
export class Teams {
  readonly auth = inject(AuthService);
  private readonly api = inject(TournamentApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly renderer = inject(Renderer2);
  private readonly document = inject(DOCUMENT);
  private lastTrigger?: HTMLElement;
  @ViewChild('createButton') private createButton?: ElementRef<HTMLButtonElement>;
  @ViewChild('dialog') private dialog?: ElementRef<HTMLElement>;
  @ViewChild('nameInput') private nameInput?: ElementRef<HTMLInputElement>;
  readonly teams = signal<Team[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal('');
  readonly saveError = signal('');
  readonly message = signal('');
  readonly isCreateModalOpen = signal(false);
  readonly expandedAccessTeamId = signal<string | null>(null);
  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [trimmedRequired, Validators.maxLength(120)]],
  });
  constructor() {
    this.load();
    afterRenderEffect(() => {
      if (this.isCreateModalOpen()) this.nameInput?.nativeElement.focus();
    });
  }

  openCreateModal(event?: Event) {
    if (event?.currentTarget instanceof HTMLElement) this.lastTrigger = event.currentTarget;
    this.form.reset();
    this.saveError.set('');
    this.message.set('');
    this.isCreateModalOpen.set(true);
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
        'button:not([disabled]), input:not([disabled])',
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
          this.closeCreateModal(true);
          this.load();
        },
        error: (error) => this.saveError.set(apiError(error)),
      });
  }
  toggleAccess(teamId: string) {
    this.expandedAccessTeamId.update((current) => (current === teamId ? null : teamId));
  }

  ngOnDestroy() {
    this.renderer.removeStyle(this.document.body, 'overflow');
  }
}
