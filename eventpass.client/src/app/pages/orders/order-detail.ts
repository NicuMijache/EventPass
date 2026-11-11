import { Component, computed, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { OrdersService } from '../../core/services/orders.service';
import { AuthService } from '../../core/services/auth.service';
import { TicketQr } from '../../components/ticket-qr/ticket-qr';
import { errorMessage } from '../../core/http-errors';
import { Order, ORDER_STATUS_LABELS } from '../../core/models/order.models';

// Pagina unei comenzi: ce ai rezervat, cât costă și cât timp mai ai până expiră rezervarea
@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, DatePipe, DecimalPipe, TicketQr],
  templateUrl: './order-detail.html',
  styleUrl: './orders.scss',
})
export class OrderDetail {
  private readonly ordersService = inject(OrdersService);

  readonly id = input.required<string>();
  // Pus de Stripe în adresă la întoarcere: „succes” sau „anulata”
  readonly plata = input<string>();

  protected readonly order = signal<Order | null>(null);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly cancelling = signal(false);
  protected readonly paying = signal(false);
  // Rambursarea are doi pași: întâi întrebăm „Ești sigur?”, abia apoi trimitem cererea
  protected readonly confirmingRefund = signal(false);
  protected readonly refunding = signal(false);
  // Retrimiterea biletelor pe email (#21)
  protected readonly resending = signal(false);
  protected readonly resendNote = signal<{ ok: boolean; text: string } | null>(null);
  private emailPollsLeft = 0;
  private readonly auth = inject(AuthService);
  protected readonly labels = ORDER_STATUS_LABELS;

  // „Acum”, actualizat în fiecare secundă pentru numărătoarea inversă
  private readonly now = signal(Date.now());

  protected readonly secondsLeft = computed(() => {
    const o = this.order();
    if (!o || o.status !== 'Pending' || !o.expiresAt) return 0;
    return Math.max(0, Math.floor((new Date(o.expiresAt).getTime() - this.now()) / 1000));
  });

  protected readonly countdown = computed(() => {
    const s = this.secondsLeft();
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  });

  private refreshedAfterExpiry = false;

  // Confirmarea plății vine prin webhook, deci după întoarcerea de pe Stripe comanda poate
  // rămâne Pending câteva secunde. Întrebăm serverul din 2 în 2 secunde, cel mult ~1 minut.
  private static readonly POLL_INTERVAL_MS = 2000;
  private static readonly POLL_MAX_ATTEMPTS = 30;
  private pollAttempts = 0;
  protected readonly confirmationDelayed = signal(false);

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    const poller = setInterval(() => {
      this.pollForPayment();
      this.pollForEmail();
    }, OrderDetail.POLL_INTERVAL_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      clearInterval(poller);
    });

    // Se încarcă de fiecare dată când se schimbă id-ul din adresă
    effect(() => this.load(Number(this.id())));

    // Când timpul expiră, cerem din nou comanda: serverul o marchează ca anulată
    effect(() => {
      const o = this.order();
      if (o?.status === 'Pending' && this.secondsLeft() === 0 && !this.refreshedAfterExpiry) {
        this.refreshedAfterExpiry = true;
        this.load(o.id);
      }
    });
  }

  // Serverul creează sesiunea Stripe; noi doar trimitem browserul pe pagina de plată
  protected pay(): void {
    const o = this.order();
    if (!o) return;
    this.paying.set(true);
    this.actionError.set(null);
    this.ordersService.checkout(o.id).subscribe({
      next: ({ url }) => window.location.assign(url),
      error: (err) => {
        this.actionError.set(errorMessage(err, 'Nu am putut porni plata. Încearcă din nou.'));
        this.paying.set(false);
      },
    });
  }

  protected cancel(): void {
    const o = this.order();
    if (!o) return;
    this.cancelling.set(true);
    this.actionError.set(null);
    this.ordersService.cancel(o.id).subscribe({
      next: (updated) => {
        this.order.set(updated);
        this.cancelling.set(false);
      },
      error: (err) => {
        this.actionError.set(errorMessage(err));
        this.cancelling.set(false);
      },
    });
  }

  protected askRefund(): void {
    this.actionError.set(null);
    this.confirmingRefund.set(true);
  }

  protected keepOrder(): void {
    this.confirmingRefund.set(false);
  }

  // Biletele se anulează și banii se întorc pe card. Nu se poate da înapoi.
  protected refund(): void {
    const o = this.order();
    if (!o || this.refunding()) return;
    this.refunding.set(true);
    this.actionError.set(null);
    this.ordersService.refund(o.id).subscribe({
      next: (updated) => {
        this.order.set(updated);
        this.refunding.set(false);
        this.confirmingRefund.set(false);
      },
      error: (err) => {
        this.actionError.set(errorMessage(err, 'Nu am putut face rambursarea. Încearcă din nou.'));
        this.refunding.set(false);
        this.confirmingRefund.set(false);
        // Starea s-ar fi putut schimba între timp (ex. un bilet scanat): o reîncărcăm
        this.refresh(o.id);
      },
    });
  }

  // „Verifică din nou” după ce întârzierea a fost afișată
  protected checkAgain(): void {
    this.pollAttempts = 0;
    this.confirmationDelayed.set(false);
    const o = this.order();
    if (o) this.refresh(o.id);
  }

  protected hasUsedTicket(order: Order): boolean {
    return order.tickets.some((t) => t.isUsed);
  }

  // Cere serverului să trimită din nou emailul cu biletele (la adresa contului)
  protected resend(): void {
    const o = this.order();
    if (!o || this.resending()) return;
    this.resending.set(true);
    this.resendNote.set(null);
    this.ordersService.resendTickets(o.id).subscribe({
      next: () => {
        this.resending.set(false);
        const email = this.auth.user()?.email;
        this.resendNote.set({
          ok: true,
          text: `Trimitem biletele${email ? ' la ' + email : ''}. Dacă nu le vezi în câteva minute, verifică și dosarul Spam.`,
        });
        // Urmărim câteva secunde până când serverul confirmă că emailul a plecat
        this.emailPollsLeft = OrderDetail.EMAIL_POLLS;
        this.refresh(o.id);
      },
      error: (err) => {
        this.resending.set(false);
        this.resendNote.set({
          ok: false,
          text: errorMessage(err, 'Nu am putut retrimite biletele. Încearcă din nou.'),
        });
      },
    });
  }

  // Scurtă formă a token-ului, ca referință (token-ul întreg va fi doar în codul QR)
  protected shortCode(token: string): string {
    return token.slice(0, 8).toUpperCase();
  }

  // După o retrimitere: reîncărcăm comanda până apare din nou „trimis” (cel mult ~20 s)
  private static readonly EMAIL_POLLS = 10;

  private pollForEmail(): void {
    const o = this.order();
    if (this.emailPollsLeft <= 0 || !o || o.status !== 'Paid') return;
    if (o.ticketsEmailedAt) {
      this.emailPollsLeft = 0;
      return;
    }
    this.emailPollsLeft--;
    this.refresh(o.id);
  }

  private pollForPayment(): void {
    const o = this.order();
    if (!o || o.status !== 'Pending' || this.plata() !== 'succes') return;

    if (this.pollAttempts >= OrderDetail.POLL_MAX_ATTEMPTS) {
      this.confirmationDelayed.set(true);
      return;
    }
    this.pollAttempts++;
    this.refresh(o.id);
  }

  // Reîncarcă comanda în liniște: o eroare trecătoare nu înlocuiește pagina cu un mesaj de eroare
  private refresh(id: number): void {
    this.ordersService.get(id).subscribe({
      next: (order) => this.order.set(order),
      error: () => undefined,
    });
  }

  private load(id: number): void {
    this.ordersService.get(id).subscribe({
      next: (order) => this.order.set(order),
      error: (err) =>
        this.loadError.set(err.status === 404 ? 'Comanda nu există.' : errorMessage(err)),
    });
  }
}
