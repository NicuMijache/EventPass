import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { OrdersService } from '../../core/services/orders.service';

// Codul QR al unui bilet. Imaginea vine de la server (protejată prin login), așa că o cerem prin
// HttpClient, o păstrăm ca blob și o afișăm printr-o adresă temporară (blob:...).
@Component({
  selector: 'app-ticket-qr',
  templateUrl: './ticket-qr.html',
  styleUrl: './ticket-qr.scss',
})
export class TicketQr {
  private readonly orders = inject(OrdersService);

  readonly orderId = input.required<number>();
  readonly ticketId = input.required<number>();
  readonly alt = input('Cod QR al biletului');

  protected readonly state = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly imageUrl = signal<string | null>(null);
  protected readonly fileName = signal('bilet.png');

  // Crește la „Încearcă din nou”, ca efectul de mai jos să ceară imaginea încă o dată
  private readonly attempt = signal(0);
  private objectUrl: string | null = null;

  constructor() {
    effect((onCleanup) => {
      const orderId = this.orderId();
      const ticketId = this.ticketId();
      this.attempt();

      this.state.set('loading');
      this.fileName.set(`bilet-${orderId}-${ticketId}.png`);
      const subscription = this.orders.ticketQr(orderId, ticketId).subscribe({
        next: (blob) => {
          this.revokeObjectUrl();
          this.objectUrl = URL.createObjectURL(blob);
          this.imageUrl.set(this.objectUrl);
          this.state.set('ready');
        },
        error: () => this.state.set('error'),
      });
      // Dacă bilețelul dispare sau se schimbă id-ul, anulăm cererea veche
      onCleanup(() => subscription.unsubscribe());
    });

    // Adresele blob: ocupă memorie până le eliberăm explicit
    inject(DestroyRef).onDestroy(() => this.revokeObjectUrl());
  }

  protected retry(): void {
    this.attempt.update((n) => n + 1);
  }

  private revokeObjectUrl(): void {
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }
}
