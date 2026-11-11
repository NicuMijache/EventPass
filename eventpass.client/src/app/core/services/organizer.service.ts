import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  AttendeeQuery,
  AttendeesPage,
  DashboardEvent,
  EventSales,
} from '../models/organizer.models';

// Panoul organizatorului (#20): vânzări, participanți, rambursări
@Injectable({ providedIn: 'root' })
export class OrganizerService {
  private readonly http = inject(HttpClient);

  dashboard(): Observable<DashboardEvent[]> {
    return this.http.get<DashboardEvent[]>('/api/organizer/dashboard');
  }

  sales(eventId: number): Observable<EventSales> {
    return this.http.get<EventSales>(`/api/organizer/events/${eventId}/sales`);
  }

  attendees(eventId: number, query: AttendeeQuery = {}): Observable<AttendeesPage> {
    let params = new HttpParams();
    if (query.q?.trim()) params = params.set('q', query.q.trim());
    if (query.filter && query.filter !== 'all') params = params.set('filter', query.filter);
    if (query.page && query.page > 1) params = params.set('page', query.page);
    return this.http.get<AttendeesPage>(`/api/organizer/events/${eventId}/attendees`, { params });
  }

  // Rambursarea unei comenzi de către organizator (ex. eveniment anulat), #19
  refundOrder(orderId: number): Observable<{ orderId: number; status: string; message?: string }> {
    return this.http.post<{ orderId: number; status: string; message?: string }>(
      `/api/manage/orders/${orderId}/refund`,
      {},
    );
  }
}
