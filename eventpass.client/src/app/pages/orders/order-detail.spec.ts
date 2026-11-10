import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeRo from '@angular/common/locales/ro';
import { Order } from '../../core/models/order.models';
import { OrderDetail } from './order-detail';

registerLocaleData(localeRo);

function paidOrder(overrides: Partial<Order> = {}): Order {
  return {
    id: 1,
    status: 'Paid',
    totalAmount: 300,
    createdAt: '2026-11-01T10:00:00Z',
    expiresAt: null,
    paidAt: '2026-11-01T10:05:00Z',
    event: {
      id: 10,
      title: 'Concert',
      venue: 'BT Arena',
      city: 'Cluj-Napoca',
      startsAt: '2026-12-20T18:00:00Z',
      imageUrl: null,
    },
    items: [
      {
        ticketTypeId: 100,
        ticketTypeName: 'Standard',
        quantity: 3,
        unitPrice: 100,
        lineTotal: 300,
      },
    ],
    tickets: [
      {
        id: 1,
        token: '3f2a9c1b-0000-4000-8000-000000000001',
        ticketTypeName: 'Standard',
        isUsed: false,
        isVoided: false,
      },
      {
        id: 2,
        token: '3f2a9c1b-0000-4000-8000-000000000002',
        ticketTypeName: 'Standard',
        isUsed: false,
        isVoided: false,
      },
      {
        id: 3,
        token: '3f2a9c1b-0000-4000-8000-000000000003',
        ticketTypeName: 'Standard',
        isUsed: false,
        isVoided: false,
      },
    ],
    ticketsEmailedAt: '2026-11-01T10:06:00Z',
    refundedAt: null,
    canRefund: true,
    refundDeadline: '2026-12-19T18:00:00Z',
    ...overrides,
  };
}

describe('OrderDetail – rambursare', () => {
  let fixture: ComponentFixture<OrderDetail>;
  let http: HttpTestingController;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const button = (label: string) =>
    Array.from(el().querySelectorAll('button')).find((b) => b.textContent?.includes(label)) as
      HTMLButtonElement | undefined;
  const orderRequests = () => http.match((r) => r.url === '/api/orders/1' && r.method === 'GET');

  function open(order: Order) {
    TestBed.configureTestingModule({
      imports: [OrderDetail],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: LOCALE_ID, useValue: 'ro' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OrderDetail);
    fixture.componentRef.setInput('id', '1');
    fixture.detectChanges();
    orderRequests()[0].flush(order);
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('o comandă plătită arată „Anulează comanda” și termenul', () => {
    open(paidOrder());

    expect(button('Anulează comanda')).toBeTruthy();
    expect(text()).toContain('Poți anula și primi banii înapoi până pe 19 decembrie 2026');
  });

  it('primul click doar întreabă; „Nu, păstrez biletele” nu trimite nimic', () => {
    open(paidOrder());

    button('Anulează comanda')!.click();
    fixture.detectChanges();
    expect(text()).toContain('Anulezi comanda?');
    expect(text()).toContain('Toate cele 3 bilete se anulează');
    expect(text()).toContain('Primești înapoi 300 lei');

    button('Nu, păstrez biletele')!.click();
    fixture.detectChanges();
    expect(text()).not.toContain('Anulezi comanda?');
    http.expectNone('/api/orders/1/refund');
  });

  it('după confirmare trimite cererea și arată comanda rambursată', () => {
    open(paidOrder());
    button('Anulează comanda')!.click();
    fixture.detectChanges();

    button('Da, anulează')!.click();
    fixture.detectChanges();
    const req = http.expectOne('/api/orders/1/refund');
    expect(req.request.method).toBe('POST');
    expect(button('Se anulează')?.disabled).toBe(true); // nu se poate apăsa de două ori

    req.flush(
      paidOrder({ status: 'Refunded', canRefund: false, refundedAt: '2026-11-10T12:00:00Z' }),
    );
    fixture.detectChanges();

    expect(text()).toContain('Ai primit banii înapoi');
    expect(text()).toContain('Banii apar pe card în câteva zile lucrătoare');
    expect(el().querySelector('.ticket-card')).toBeNull(); // codurile QR nu se mai arată
    expect(button('Anulează comanda')).toBeUndefined();
  });

  it('un refuz de la server (ex. bilet scanat între timp) arată mesajul și reîncarcă comanda', () => {
    open(paidOrder());
    button('Anulează comanda')!.click();
    fixture.detectChanges();
    button('Da, anulează')!.click();

    http
      .expectOne('/api/orders/1/refund')
      .flush(
        {
          message:
            'Un bilet din comandă a fost deja folosit la intrare, așa că nu mai poate fi rambursată.',
        },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain(
      'a fost deja folosit la intrare',
    );
    expect(orderRequests().length).toBe(1); // pagina cere din nou comanda, ca să arate starea reală
  });

  it('cu un bilet folosit nu oferă anularea și spune de ce', () => {
    const order = paidOrder({ canRefund: false });
    order.tickets[0].isUsed = true;
    open(order);

    expect(button('Anulează comanda')).toBeUndefined();
    expect(text()).toContain('un bilet a fost folosit la intrare');
  });

  it('după termen nu oferă anularea', () => {
    open(paidOrder({ canRefund: false }));

    expect(button('Anulează comanda')).toBeUndefined();
    expect(text()).toContain('termenul a trecut');
  });

  it('rambursarea în curs explică faptul că se termină singură', () => {
    open(paidOrder({ status: 'RefundPending', canRefund: false }));

    expect(text()).toContain('Rambursarea e în curs');
    expect(text()).toContain('se finalizează automat');
    expect(el().querySelector('.ticket-card')).toBeNull();
  });
});
