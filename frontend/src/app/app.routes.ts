import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Entrar | Brakvyo',
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'cadastro',
    title: 'Cadastro | Brakvyo',
    loadComponent: () => import('./features/auth/register').then((m) => m.Register),
  },
  { path: '', redirectTo: 'campeonatos', pathMatch: 'full' },
  {
    path: 'campeonatos',
    title: 'Campeonatos | Torneio',
    loadComponent: () => import('./features/tournaments/tournaments').then((m) => m.Tournaments),
  },
  {
    path: 'campeonatos/:id/agenda',
    title: 'Agenda do campeonato | Torneio',
    loadComponent: () => import('./features/scheduling/scheduling').then((m) => m.Scheduling),
  },
  {
    path: 'campeonatos/:id',
    title: 'Detalhes do campeonato | Torneio',
    loadComponent: () =>
      import('./features/tournaments/tournament-detail').then((m) => m.TournamentDetail),
  },
  {
    path: 'equipes',
    title: 'Equipes | Torneio',
    loadComponent: () => import('./features/teams/teams').then((m) => m.Teams),
  },
  { path: '**', redirectTo: 'campeonatos' },
];
