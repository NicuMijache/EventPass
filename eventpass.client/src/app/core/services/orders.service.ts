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

  cancel(id: number): Observable<Order> {
    return this.http.post<Order>(`/api/orders/${id}/cancel`, {});
  }
}
