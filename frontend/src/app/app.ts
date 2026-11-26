import {
  Component,
  DestroyRef,
  ElementRef,
  HostListener,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { AuthService } from './core/auth.service';

import { PageFragment } from './shared/page-fragment';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, PageFragment],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly auth = inject(AuthService);
  readonly menuOpen = signal(false);
  readonly userMenuOpen = signal(false);
  @ViewChild('userMenuButton') private userMenuButton?: ElementRef<HTMLButtonElement>;
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
        this.userMenuOpen.set(false);
        setTimeout(() => {
          if (window.location.hash) return;
          document.querySelector<HTMLElement>('#conteudo')?.focus({ preventScroll: true });
          window.scrollTo({ top: 0 });
        });
      });
  }
  toggleMenu() {
    this.menuOpen.update((open) => !open);
  }
  toggleUserMenu() {
    this.userMenuOpen.update((open) => !open);
  }
  closeUserMenu(restoreFocus = false) {
    if (!this.userMenuOpen()) return;
    this.userMenuOpen.set(false);
    if (restoreFocus) setTimeout(() => this.userMenuButton?.nativeElement.focus());
  }
  @HostListener('document:keydown.escape')
  onEscape() {
    this.menuOpen.set(false);
    this.closeUserMenu(true);
  }
  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    if (!(event.target as HTMLElement).closest('.account-menu')) this.closeUserMenu();
  }
  logout() {
    this.userMenuOpen.set(false);
    this.auth.logout().subscribe(() => void this.router.navigate(['/campeonatos']));
  }
}
