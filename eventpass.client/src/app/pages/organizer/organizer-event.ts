import { Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, Subscription } from 'rxjs';
import { OrganizerService } from '../../core/services/organizer.service';
import {
  Attendee,
  AttendeeFilter,
  AttendeesPage,
  EventSales,
  percent,
} from '../../core/models/organizer.models';
import { errorMessage } from '../../core/http-errors';

export const FILTERS: { value: AttendeeFilter; label: string }[] = [
  { value: 'all', label: 'Toți' },
  { value: 'validated', label: 'Au intrat' },
  { value: 'notValidated', label: 'Încă n-au intrat' },
  { value: 'refunded', label: 'Rambursați' },
];

// Un eveniment în panoul organizatorului: vânzări pe categorii și lista participanților (#20)
@Component({
  selector: 'app-organizer-event',
  imports: [RouterLink, DatePipe, DecimalPipe, ReactiveFormsModule],
  templateUrl: './organizer-event.html',
  styleUrl: './organizer.scss',
})
export class OrganizerEvent {
  private readonly organizer = inject(OrganizerService);
  private readonly destroyRef = inject(DestroyRef);

  readonly id = input.required<string>();

  protected readonly sales = signal<EventSales | null>(null);
  protected readonly loadError = signal<string | null>(null);

  protected readonly attendees = signal<AttendeesPage | null>(null);
  protected readonly attendeesError = signal<string | null>(null);
  protected readonly loadingAttendees = signal(false);
  protected readonly filter = signal<AttendeeFilter>('all');
  protected readonly page = signal(1);
  protected readonly search = new FormControl('', { nonNullable: true });
  protected readonly filters = FILTERS;
  protected readonly percent = percent;

  // Rambursare: comanda pentru care se cere confirmarea, cea în curs și rezultatul
  protected readonly confirmOrder = signal<number | null>(null);
  protected readonly refunding = signal<number | null>(null);
  protected readonly notice = signal<{ ok: boolean; text: string } | null>(null);

  private attendeesRequest: Subscription | null = null;

  constructor() {
    // Se reîncarcă doar când se schimbă id-ul din adresă. untracked: filtrul și pagina citite în interior
    // NU devin motive de reîncărcare aici (de ele se ocupă setFilter / goTo), altfel s-ar încărca de două ori
    effect(() => {
      const id = Number(this.id());
      untracked(() => {
        this.loadSales(id);
        this.loadAttendees();
      });
    });

    // Căutăm abia după o scurtă pauză în tastare, nu la fiecare literă
    this.search.valueChanges
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page.set(1);
        this.loadAttendees();
      });
  }

  protected setFilter(value: AttendeeFilter): void {
    if (this.filter() === value) return;
    this.filter.set(value);
    this.page.set(1);
    this.loadAttendees();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.loadAttendees();
  }

  protected refresh(): void {
    this.notice.set(null);
    this.reload();
  }

  protected lastPage(p: AttendeesPage): number {
    return Math.max(1, Math.ceil(p.total / p.pageSize));
  }

  // Butonul de rambursare apare o singură dată pe comandă (pe primul ei bilet din listă)
  protected isFirstOfOrder(items: Attendee[], index: number): boolean {
    return index === 0 || items[index - 1].orderId !== items[index].orderId;
  }

  protected ticketsInOrder(items: Attendee[], orderId: number): number {
    return items.filter((a) => a.orderId === orderId).length;
  }

  protected askRefund(orderId: number): void {
    this.notice.set(null);
    this.confirmOrder.set(orderId);
  }

  protected cancelRefund(): void {
    this.confirmOrder.set(null);
  }

  protected refund(orderId: number): void {
    if (this.refunding() !== null) return;
    this.refunding.set(orderId);
    this.organizer.refundOrder(orderId).subscribe({
      next: (res) => {
        this.refunding.set(null);
        this.confirmOrder.set(null);
        this.notice.set({
          ok: true,
          text:
            res.status === 'Refunded'
              ? `Comanda #${orderId} a fost rambursată; biletele ei sunt anulate.`
              : `Biletele comenzii #${orderId} sunt anulate; rambursarea se finalizează automat.`,
        });
        this.reload();
      },
      error: (err) => {
        this.refunding.set(null);
        this.confirmOrder.set(null);
        this.notice.set({
          ok: false,
          text: errorMessage(err, 'Rambursarea nu a reușit. Încearcă din nou.'),
        });
        this.reload();
      },
    });
  }

  // Reîncarcă cifrele și lista, păstrând mesajul afișat
  private reload(): void {
    this.loadSales(Number(this.id()));
    this.loadAttendees();
  }

  private loadSales(id: number): void {
    this.organizer.sales(id).subscribe({
      next: (sales) => this.sales.set(sales),
      error: (err) =>
        this.loadError.set(
          err.status === 404 ? 'Evenimentul nu există sau nu este al tău.' : errorMessage(err),
        ),
    });
  }

  private loadAttendees(): void {
    // Dacă o cerere mai veche n-a răspuns încă, o abandonăm: altfel ar putea suprascrie rezultatul nou
    this.attendeesRequest?.unsubscribe();
    this.loadingAttendees.set(true);
    this.attendeesError.set(null);
    this.attendeesRequest = this.organizer
      .attendees(Number(this.id()), {
        q: this.search.value,
        filter: this.filter(),
        page: this.page(),
      })
      .subscribe({
        next: (page) => {
          this.attendees.set(page);
          this.loadingAttendees.set(false);
        },
        error: (err) => {
          this.attendeesError.set(errorMessage(err, 'Nu am putut încărca participanții.'));
          this.loadingAttendees.set(false);
        },
      });
  }
}
