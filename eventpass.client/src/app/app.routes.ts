import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'EventPass – Evenimente',
    loadComponent: () => import('./pages/home/home').then((m) => m.Home),
  },
  {
    path: 'evenimente/:id',
    title: 'EventPass – Eveniment',
    loadComponent: () => import('./pages/event-detail/event-detail').then((m) => m.EventDetail),
  },
  // Orice adresă necunoscută duce la pagina principală
  { path: '**', redirectTo: '' },
];
