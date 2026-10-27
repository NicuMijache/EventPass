import { Component, input } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { PublicEventCard } from '../../core/models/event.models';

// Un poster din grila de pe pagina principală
@Component({
  selector: 'app-event-card',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './event-card.html',
  styleUrl: './event-card.scss',
})
export class EventCard {
  readonly event = input.required<PublicEventCard>();
}
