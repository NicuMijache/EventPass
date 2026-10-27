import { Component, computed, effect, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Title } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { EventsService } from '../../core/services/events.service';
import { PublicEventDetail } from '../../core/models/event.models';

type DetailState =
  | { status: 'loading' }
  | { status: 'not-found' }
  | { status: 'error' }
  | { status: 'ready'; event: PublicEventDetail };

// Sub acest număr de locuri rămase afișăm „Ultimele X locuri”
const LOW_STOCK = 20;

@Component({
  selector: 'app-event-detail',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './event-detail.html',
  styleUrl: './event-detail.scss',
})
export class EventDetail {
  private readonly eventsService = inject(EventsService);
  private readonly title = inject(Title);

  // Vine din adresă (/evenimente/:id) datorită withComponentInputBinding()
  readonly id = input.required<string>();

  protected readonly lowStock = LOW_STOCK;

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

  constructor() {
    // Titlul din tab-ul browserului devine numele evenimentului
    effect(() => {
      const s = this.state();
      if (s.status === 'ready') {
        this.title.setTitle(`${s.event.title} – EventPass`);
      }
    });
  }
}
