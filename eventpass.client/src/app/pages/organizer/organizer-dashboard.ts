import { Component, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { OrganizerService } from '../../core/services/organizer.service';
import { DashboardEvent, percent } from '../../core/models/organizer.models';
import { errorMessage } from '../../core/http-errors';

// Panoul organizatorului: toate evenimentele lui, cu vânzările pe scurt (#20)
@Component({
  selector: 'app-organizer-dashboard',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './organizer-dashboard.html',
  styleUrl: './organizer.scss',
})
export class OrganizerDashboard {
  private readonly organizer = inject(OrganizerService);

  protected readonly events = signal<DashboardEvent[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly percent = percent;

  constructor() {
    this.organizer.dashboard().subscribe({
      next: (events) => this.events.set(events),
      error: (err) => this.error.set(errorMessage(err, 'Nu am putut încărca evenimentele.')),
    });
  }

  // Evenimentul a avut deja loc (îl arătăm mai estompat)
  protected isPast(event: DashboardEvent): boolean {
    return new Date(event.startsAt).getTime() < Date.now();
  }
}
