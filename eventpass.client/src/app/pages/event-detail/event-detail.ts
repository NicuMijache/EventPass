import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Title } from '@angular/platform-browser';
import { Router, RouterLink } from '@angular/router';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { EventsService } from '../../core/services/events.service';
import { OrdersService } from '../../core/services/orders.service';
import { AuthService } from '../../core/services/auth.service';
import { errorMessage } from '../../core/http-errors';
import { PublicEventDetail, PublicTicketType } from '../../core/models/event.models';

type DetailState =
  | { status: 'loading' }
  | { status: 'not-found' }
  | { status: 'error' }
  | { status: 'ready'; event: PublicEventDetail };

// Sub acest număr de locuri rămase afișăm „Ultimele X locuri”
const LOW_STOCK = 20;
// La fel ca pe server (OrdersController.MaxTicketsPerOrder)
const MAX_TICKETS = 10;
// Selecția păstrată cât timp utilizatorul trece prin login
const SELECTION_KEY = 'eventpass.selection';

@Component({
  selector: 'app-event-detail',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './event-detail.html',
  styleUrl: './event-detail.scss',
})
export class EventDetail {
  private readonly eventsService = inject(EventsService);
  private readonly ordersService = inject(OrdersService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly title = inject(Title);

  // Vine din adresă (/evenimente/:id) datorită withComponentInputBinding()
  readonly id = input.required<string>();

  protected readonly lowStock = LOW_STOCK;
  protected readonly maxTickets = MAX_TICKETS;

  protected readonly state = toSignal(
    toObservable(computed(() => Number(this.id()))).pipe(
      switchMap((id) =>
        this.eventsService.getEvent(id).pipe(
          map((event): DetailState => ({ status: 'ready', event })),
          catchError((err: HttpErrorResponse) =>
            of<DetailState>({ status: err.status === 404 ? 'not-found' : 'error' })
          ),
          startWith<DetailState>({ status: 'loading' })
        )
      )
    ),
    { initialValue: { status: 'loading' } as DetailState }
  );

  // Câte bilete din fiecare tip a ales utilizatorul: { ticketTypeId: cantitate }
  protected readonly quantities = signal<Record<number, number>>({});
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly totalTickets = computed(() =>
    Object.values(this.quantities()).reduce((sum, q) => sum + q, 0)
  );

  // Doar o estimare pentru afișare; prețul final îl calculează serverul
  protected readonly totalPrice = computed(() => {
    const s = this.state();
    if (s.status !== 'ready') return 0;
    return s.event.ticketTypes.reduce((sum, t) => sum + t.price * (this.quantities()[t.id] ?? 0), 0);
  });

  constructor() {
    effect(() => {
      const s = this.state();
      if (s.status === 'ready') {
        // Titlul din tab-ul browserului devine numele evenimentului
        this.title.setTitle(`${s.event.title} – EventPass`);
        this.restoreSelection(s.event.id);
      }
    });
  }

  protected quantityOf(ticket: PublicTicketType): number {
    return this.quantities()[ticket.id] ?? 0;
  }

  protected canIncrease(ticket: PublicTicketType): boolean {
    return this.quantityOf(ticket) < ticket.available && this.totalTickets() < MAX_TICKETS;
  }

  protected change(ticket: PublicTicketType, delta: number): void {
    const next = Math.max(0, Math.min(ticket.available, this.quantityOf(ticket) + delta));
    this.quantities.update((q) => ({ ...q, [ticket.id]: next }));
    this.error.set(null);
  }

  protected reserve(eventId: number): void {
    const items = Object.entries(this.quantities())
      .filter(([, quantity]) => quantity > 0)
      .map(([ticketTypeId, quantity]) => ({ ticketTypeId: Number(ticketTypeId), quantity }));
    if (items.length === 0) return;

    // Fără cont: ținem minte selecția și trimitem la login, cu întoarcere aici
    if (!this.auth.token()) {
      this.saveSelection(eventId);
      this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
      return;
    }

    this.submitting.set(true);
    this.error.set(null);
    this.ordersService.create({ eventId, items }).subscribe({
      next: (order) => {
        this.clearSelection();
        this.router.navigate(['/comenzi', order.id]);
      },
      error: (err) => {
        this.error.set(errorMessage(err, 'Nu am putut rezerva biletele. Încearcă din nou.'));
        this.submitting.set(false);
      },
    });
  }

  private saveSelection(eventId: number): void {
    try {
      sessionStorage.setItem(SELECTION_KEY, JSON.stringify({ eventId, quantities: this.quantities() }));
    } catch {
      // fără sessionStorage, utilizatorul va alege din nou biletele după login
    }
  }

  private restoreSelection(eventId: number): void {
    try {
      const raw = sessionStorage.getItem(SELECTION_KEY);
      if (!raw) return;
      const saved = JSON.parse(raw) as { eventId: number; quantities: Record<number, number> };
      if (saved.eventId === eventId) {
        this.quantities.set(saved.quantities);
      }
      sessionStorage.removeItem(SELECTION_KEY);
    } catch {
      // selecția salvată nu e validă; o ignorăm
    }
  }

  private clearSelection(): void {
    try {
      sessionStorage.removeItem(SELECTION_KEY);
    } catch {
      // nimic de șters
    }
  }
}
