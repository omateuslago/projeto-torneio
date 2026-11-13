import { Component, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { finalize } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AccessApi, AccessMembers } from '../../core/access-api';
import { AuthService } from '../../core/auth.service';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-access-management',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './access-management.html',
})
export class AccessManagement implements OnInit {
  readonly kind = input.required<'tournament' | 'team'>();
  readonly targetId = input.required<string>();
  readonly title = input.required<string>();
  readonly auth = inject(AuthService);
  private readonly api = inject(AccessApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly data = signal<AccessMembers>({ members: [], pendingInvitations: [] });
  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly invitationLink = signal('');
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
  });

  ngOnInit() {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');
    this.api
      .members(this.kind(), this.targetId())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: (data) => this.data.set(data),
        error: (error) => this.error.set(apiError(error)),
      });
  }
  invite() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.api
      .invite(this.kind(), this.targetId(), this.form.getRawValue().email.trim())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busy.set(false)),
      )
      .subscribe({
        next: (result) => {
          this.invitationLink.set(`${window.location.origin}${result.relativePath}`);
          this.form.reset();
          this.load();
        },
        error: (error) => this.error.set(apiError(error)),
      });
  }
  revoke(id: string) {
    this.api.revoke(this.kind(), this.targetId(), id).subscribe({
      next: () => this.load(),
      error: (error) => this.error.set(apiError(error)),
    });
  }
  remove(userId: string) {
    this.api.remove(this.kind(), this.targetId(), userId).subscribe({
      next: () => this.load(),
      error: (error) => this.error.set(apiError(error)),
    });
  }
  copy() {
    if (this.invitationLink()) void navigator.clipboard?.writeText(this.invitationLink());
  }
}
