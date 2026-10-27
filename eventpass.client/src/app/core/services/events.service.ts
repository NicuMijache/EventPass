import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { EventFilters, EventQuery, PublicEventCard, PublicEventDetail } from '../models/event.models';

// Apelurile către API-ul public de evenimente. Adresele încep cu /api,
// iar în dezvoltare proxy-ul Angular le trimite la serverul .NET (localhost:5003).
@Injectable({ providedIn: 'root' })
export class EventsService {
  private readonly http = inject(HttpClient);

  getEvents(query: EventQuery = {}): Observable<PublicEventCard[]> {
    let params = new HttpParams();
    query.cities?.forEach((city) => (params = params.append('city', city)));
    query.venues?.forEach((venue) => (params = params.append('venue', venue)));
    if (query.search?.trim()) {
      params = params.set('search', query.search.trim());
    }
    return this.http.get<PublicEventCard[]>('/api/events', { params });
  }

  getFilters(): Observable<EventFilters> {
    return this.http.get<EventFilters>('/api/events/filters');
  }

  getEvent(id: number): Observable<PublicEventDetail> {
    return this.http.get<PublicEventDetail>(`/api/events/${id}`);
  }
}
