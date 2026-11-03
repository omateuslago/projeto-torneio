import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'campeonatos', pathMatch: 'full' },
  { path: 'campeonatos', title: 'Campeonatos | Torneio', loadComponent: () => import('./features/tournaments/tournaments').then(m => m.Tournaments) },
  { path: 'campeonatos/:id', title: 'Detalhes do campeonato | Torneio', loadComponent: () => import('./features/tournaments/tournament-detail').then(m => m.TournamentDetail) },
  { path: '**', redirectTo: 'campeonatos' },
];
