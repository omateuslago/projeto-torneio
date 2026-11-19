import { Component, DestroyRef, effect, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AccessApi, InvitationPreview } from '../../core/access-api';
import { AuthService } from '../../core/auth.service';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-accept-invitation',
  imports: [RouterLink, DatePipe],
  templateUrl: './accept-invitation.html',
})
export class AcceptInvitation {
  readonly auth = inject(AuthService);
  private readonly api = inject(AccessApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token') ?? '';
  readonly returnUrl = `/convites/aceitar?token=${encodeURIComponent(this.token)}`;
  readonly preview = signal<InvitationPreview | null>(null);
  readonly loading = signal(false);
  readonly accepting = signal(false);
  readonly error = signal('');
  private readonly previewRequested = signal(false);

  constructor() {
    effect(() => {
      if (this.auth.isAuthenticated() && !this.previewRequested()) {
        this.previewRequested.set(true);
        this.load();
      }
    });
  }
  load() {
    this.loading.set(true);
    this.error.set('');
    this.api
      .preview(this.token)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (preview) => this.preview.set(preview),
        error: (error) => this.error.set(apiError(error)),
      });
  }
  accept() {
    if (this.accepting()) return;
    this.accepting.set(true);
    this.error.set('');
    this.api
      .accept(this.token)
      .pipe(
        switchMap((accepted) => this.auth.refresh().pipe(switchMap(() => [accepted]))),
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.accepting.set(false)),
      )
      .subscribe({
        next: (accepted) =>
          void this.router.navigate(
            accepted.type === 'TournamentOrganizer'
              ? ['/campeonatos', accepted.targetId]
              : ['/equipes'],
          ),
        error: (error) => this.error.set(apiError(error)),
      });
  }
}
