import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth.service';
import { apiError } from '../../core/api-error';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
  readonly form = inject(FormBuilder).nonNullable.group({
    displayName: ['', [Validators.required, Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmation: ['', Validators.required],
  });
  submit() {
    this.form.markAllAsTouched();
    const value = this.form.getRawValue();
    if (this.form.invalid || value.password !== value.confirmation || this.busy()) {
      if (value.password !== value.confirmation) this.error.set('As senhas não coincidem.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    this.auth
      .register(value.email, value.password, value.displayName.trim())
      .pipe(finalize(() => this.busy.set(false)))
      .subscribe({
        next: () => void this.router.navigateByUrl(this.returnUrl || '/campeonatos'),
        error: (e) => this.error.set(apiError(e)),
      });
  }
}
