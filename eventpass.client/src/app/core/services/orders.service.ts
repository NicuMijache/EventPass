import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { CreateOrderRequest, Order } from '../models/order.models';

// Comenzile utilizatorului logat (tokenul îl adaugă automat authInterceptor)
@Injectable({ providedIn: 'root' })
export class OrdersService {
  private readonly http = inject(HttpClient);

  create(request: CreateOrderRequest): Observable<Order> {
    return this.http.post<Order>('/api/orders', request);
  }

  mine(): Observable<Order[]> {
    return this.http.get<Order[]>('/api/orders/mine');
  }

  get(id: number): Observable<Order> {
    return this.http.get<Order>(`/api/orders/${id}`);
  }

  // Cere serverului o sesiune de plată Stripe și primește adresa paginii de plată
  checkout(id: number): Observable<{ url: string }> {
    return this.http.post<{ url: string }>(`/api/orders/${id}/checkout`, {});
  }

  // Imaginea QR a unui bilet, ca fișier în memorie (blob). Nu o putem pune direct într-un <img src="...">,
  // pentru că o etichetă <img> nu trimite tokenul de login; HttpClient îl adaugă singur (interceptorul).
  ticketQr(orderId: number, ticketId: number): Observable<Blob> {
    return this.http.get(`/api/orders/${orderId}/tickets/${ticketId}/qr`, { responseType: 'blob' });
  }

  // Renunță la o comandă PLĂTITĂ: biletele se anulează și banii se întorc pe card (#19)
  refund(id: number): Observable<Order> {
    return this.http.post<Order>(`/api/orders/${id}/refund`, {});
  }

  cancel(id: number): Observable<Order> {
    return this.http.post<Order>(`/api/orders/${id}/cancel`, {});
  }
}
