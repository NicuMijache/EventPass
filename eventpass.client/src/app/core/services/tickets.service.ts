import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { TicketValidation } from '../models/ticket.models';

// Biletele, din partea personalului de la intrare
@Injectable({ providedIn: 'root' })
export class TicketsService {
  private readonly http = inject(HttpClient);

  // Verifică un bilet și, dacă e bun, îl marchează ca folosit. Codul: tokenul întreg sau codul scurt (8 caractere).
  validate(code: string): Observable<TicketValidation> {
    return this.http.post<TicketValidation>('/api/tickets/validate', { code });
  }
}
