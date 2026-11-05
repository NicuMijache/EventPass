import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/guards/auth.guard';

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
  {
    path: 'login',
    title: 'EventPass – Intră în cont',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/auth/login').then((m) => m.Login),
  },
  {
    path: 'cont-nou',
    title: 'EventPass – Creează cont',
    canActivate: [guestGuard],
    loadComponent: () => import('./pages/auth/register').then((m) => m.Register),
  },
  {
    path: 'comenzi',
    title: 'EventPass – Comenzile mele',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/orders/my-orders').then((m) => m.MyOrders),
  },
  {
    path: 'comenzi/:id',
    title: 'EventPass – Comanda ta',
    canActivate: [authGuard],
    loadComponent: () => import('./pages/orders/order-detail').then((m) => m.OrderDetail),
  },
  // Orice adresă necunoscută duce la pagina principală
  { path: '**', redirectTo: '' },
];
