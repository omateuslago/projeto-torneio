import { Component, DestroyRef, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly auth = inject(AuthService);
  readonly menuOpen = signal(false);
  private readonly router = inject(Router);
  constructor() {
    this.auth.refresh().subscribe();
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => {
        this.menuOpen.set(false);
        setTimeout(() => {
          document.querySelector<HTMLElement>('#conteudo')?.focus({ preventScroll: true });
          window.scrollTo({ top: 0 });
        });
      });
  }
  toggleMenu() {
    this.menuOpen.update((open) => !open);
  }
  logout() {
    this.auth.logout().subscribe(() => void this.router.navigate(['/campeonatos']));
  }
}
