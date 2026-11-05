import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { catchError, map, of, startWith } from 'rxjs';
import { OrdersService } from '../../core/services/orders.service';
import { Order, ORDER_STATUS_LABELS } from '../../core/models/order.models';

type ListState = { status: 'loading' } | { status: 'error' } | { status: 'ready'; orders: Order[] };

@Component({
  selector: 'app-my-orders',
  imports: [RouterLink, DatePipe, DecimalPipe],
  templateUrl: './my-orders.html',
  styleUrl: './orders.scss',
})
export class MyOrders {
  protected readonly labels = ORDER_STATUS_LABELS;

  protected readonly state = toSignal(
    inject(OrdersService)
      .mine()
      .pipe(
        map((orders): ListState => ({ status: 'ready', orders })),
        catchError(() => of<ListState>({ status: 'error' })),
        startWith<ListState>({ status: 'loading' })
      ),
    { initialValue: { status: 'loading' } as ListState }
  );

  protected ticketCount(order: Order): number {
    return order.items.reduce((sum, line) => sum + line.quantity, 0);
  }
}
