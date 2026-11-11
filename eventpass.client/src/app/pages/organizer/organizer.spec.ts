import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LOCALE_ID } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeRo from '@angular/common/locales/ro';
import {
  Attendee,
  AttendeesPage,
  DashboardEvent,
  EventSales,
  percent,
} from '../../core/models/organizer.models';
import { OrganizerDashboard } from './organizer-dashboard';
import { OrganizerEvent } from './organizer-event';

registerLocaleData(localeRo);

const totals = {
  capacity: 15,
  ticketsSold: 3,
  reserved: 1,
  available: 11,
  revenue: 300,
  validated: 1,
  refundedOrders: 1,
  refundedAmount: 200,
};

function sales(): EventSales {
  return {
    id: 10,
    title: 'Concert',
    venue: 'BT Arena',
    city: 'Cluj-Napoca',
    startsAt: '2027-01-20T18:00:00Z',
    isPublished: true,
    totals,
    ticketTypes: [
      {
        id: 100,
        name: 'Standard',
        price: 50,
        capacity: 10,
        ticketsSold: 2,
        reserved: 1,
        available: 7,
        revenue: 100,
        validated: 1,
      },
      {
        id: 101,
        name: 'VIP',
        price: 200,
        capacity: 5,
        ticketsSold: 1,
        reserved: 0,
        available: 4,
        revenue: 200,
        validated: 0,
      },
    ],
  };
}

function attendee(overrides: Partial<Attendee>): Attendee {
  return {
    ticketId: 1,
    shortCode: 'AA000001',
    ticketTypeName: 'Standard',
    holderName: 'Ana Pop',
    holderEmail: 'ana@example.com',
    orderId: 1,
    orderStatus: 'Paid',
    paidAt: '2026-11-01T10:00:00Z',
    isUsed: false,
    usedAt: null,
    isVoided: false,
    canRefund: true,
    ...overrides,
  };
}

function page(items: Attendee[], total = items.length, p = 1, pageSize = 50): AttendeesPage {
  return { total, page: p, pageSize, items };
}

const providers = () => [
  provideHttpClient(),
  provideHttpClientTesting(),
  provideRouter([]),
  { provide: LOCALE_ID, useValue: 'ro' },
];

describe('percent', () => {
  it('rotunjește și nu împarte la zero', () => {
    expect(percent(1, 3)).toBe(33);
    expect(percent(2, 3)).toBe(67);
    expect(percent(5, 0)).toBe(0);
    expect(percent(10, 10)).toBe(100);
  });
});

describe('OrganizerDashboard (lista evenimentelor)', () => {
  let fixture: ComponentFixture<OrganizerDashboard>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [OrganizerDashboard], providers: providers() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OrganizerDashboard);
    fixture.detectChanges();
  });

  it('arată fiecare eveniment cu vândute, încasări și validate, cu link spre detalii', () => {
    const events: DashboardEvent[] = [
      {
        id: 10,
        title: 'Concert',
        venue: 'BT Arena',
        city: 'Cluj-Napoca',
        startsAt: '2027-01-20T18:00:00Z',
        isPublished: true,
        totals,
      },
      {
        id: 11,
        title: 'Ciornă',
        venue: 'Sala',
        city: 'Iași',
        startsAt: '2027-02-01T18:00:00Z',
        isPublished: false,
        totals: { ...totals, ticketsSold: 0, revenue: 0, validated: 0 },
      },
    ];
    http.expectOne('/api/organizer/dashboard').flush(events);
    fixture.detectChanges();

    const rows = el().querySelectorAll('.event-row');
    expect(rows.length).toBe(2);
    expect(rows[0].getAttribute('href')).toBe('/organizator/evenimente/10');
    const first = (rows[0].textContent ?? '').replace(/\s+/g, ' ');
    expect(first).toContain('Concert');
    expect(first).toContain('3 / 15');
    expect(first).toContain('300 lei');
    expect(first).toContain('1 / 3');
    expect((rows[0].querySelector('.meter__fill') as HTMLElement).style.width).toBe('20%');
    expect(rows[1].textContent).toContain('Nepublicat');
  });

  it('fără evenimente, explică ce e de făcut', () => {
    http.expectOne('/api/organizer/dashboard').flush([]);
    fixture.detectChanges();

    expect(el().textContent).toContain('Nu ai încă niciun eveniment');
  });

  it('o eroare de la server e afișată', () => {
    http
      .expectOne('/api/organizer/dashboard')
      .flush({ message: 'Problemă' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('Problemă');
  });
});

describe('OrganizerEvent (vânzările unui eveniment)', () => {
  let fixture: ComponentFixture<OrganizerEvent>;
  let http: HttpTestingController;
  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const attendeeRequests = () => http.match((r) => r.url === '/api/organizer/events/10/attendees');
  const button = (label: string) =>
    Array.from(el().querySelectorAll('button')).find((b) =>
      b.textContent?.trim().startsWith(label),
    ) as HTMLButtonElement | undefined;

  function open(items: Attendee[] = [attendee({})]) {
    TestBed.configureTestingModule({ imports: [OrganizerEvent], providers: providers() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OrganizerEvent);
    fixture.componentRef.setInput('id', '10');
    fixture.detectChanges();
    http.expectOne('/api/organizer/events/10/sales').flush(sales());
    const requests = attendeeRequests();
    expect(requests.length).toBe(1); // o singură cerere la deschidere (nu două)
    requests[0].flush(page(items));
    fixture.detectChanges();
  }

  afterEach(() => fixture?.destroy());

  it('arată cifrele principale și fiecare categorie', () => {
    open();

    expect(text()).toContain('Concert');
    const kpis = Array.from(el().querySelectorAll('.kpi')).map((k) =>
      (k.textContent ?? '').replace(/\s+/g, ' '),
    );
    expect(kpis[0]).toContain('3 din 15');
    expect(kpis[0]).toContain('11 libere');
    expect(kpis[0]).toContain('1 rezervate');
    expect(kpis[1]).toContain('300 lei');
    expect(kpis[1]).toContain('200 lei rambursați (1 comandă)');
    expect(kpis[2]).toContain('1 din 3');
    expect(kpis[2]).toContain('33%');
    const types = Array.from(el().querySelectorAll('.type')).map((t) =>
      (t.textContent ?? '').replace(/\s+/g, ' '),
    );
    expect(types[0]).toContain('Standard');
    expect(types[0]).toContain('2 / 10');
    expect(types[1]).toContain('VIP');
    expect(types[1]).toContain('200 lei');
  });

  it('lista participanților: stări diferite (a intrat, valabil, rambursat)', () => {
    open([
      attendee({
        ticketId: 3,
        orderId: 3,
        holderName: 'Ion',
        orderStatus: 'Refunded',
        isVoided: true,
        canRefund: false,
      }),
      attendee({ ticketId: 1, isUsed: true, usedAt: '2026-11-11T18:30:00Z', canRefund: false }),
      attendee({ ticketId: 2, shortCode: 'BB000002', canRefund: false }),
    ]);

    const rows = Array.from(el().querySelectorAll('.people__list .people__row'));
    expect(rows.length).toBe(3);
    expect(rows[0].classList).toContain('people__row--void');
    expect(rows[0].textContent).toContain('Rambursat');
    expect(rows[1].textContent).toMatch(/A intrat \d\d:30/);
    expect(rows[2].textContent).toContain('Valabil');
    expect(el().querySelector('.link-button')).toBeNull(); // nimic de rambursat
  });

  it('căutarea așteaptă o pauză în tastare, apoi cere prima pagină', async () => {
    open();
    vi.useFakeTimers();
    const input = el().querySelector('input[type=search]') as HTMLInputElement;

    input.value = 'an';
    input.dispatchEvent(new Event('input'));
    input.value = 'ana';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(299);
    expect(attendeeRequests().length).toBe(0);
    vi.advanceTimersByTime(1);
    vi.useRealTimers();

    const requests = attendeeRequests();
    expect(requests.length).toBe(1); // o singură cerere pentru „ana”, nu câte una la fiecare literă
    expect(requests[0].request.params.get('q')).toBe('ana');
    expect(requests[0].request.params.has('page')).toBe(false);
  });

  it('filtrele trimit filtrul ales și revin la prima pagină', () => {
    open();

    button('Au intrat')!.click();
    fixture.detectChanges();
    let req = attendeeRequests();
    expect(req.length).toBe(1);
    expect(req[0].request.params.get('filter')).toBe('validated');
    req[0].flush(page([]));
    fixture.detectChanges();
    expect(button('Au intrat')!.getAttribute('aria-pressed')).toBe('true');
    expect(text()).toContain('Niciun participant nu se potrivește');

    button('Toți')!.click();
    req = attendeeRequests();
    expect(req[0].request.params.has('filter')).toBe(false);
  });

  it('paginile: „Înainte” cere pagina 2, iar pe ultima pagină butonul e dezactivat', () => {
    open();
    fixture.componentInstance['attendees'].set(page([attendee({})], 120, 1, 50));
    fixture.detectChanges();
    expect(text()).toContain('1–1 din 120');

    button('Înainte')!.click();
    const req: TestRequest = attendeeRequests()[0];
    expect(req.request.params.get('page')).toBe('2');
    req.flush(page([attendee({})], 120, 3, 50));
    fixture.detectChanges();
    expect(button('Înainte')!.disabled).toBe(true);
  });

  it('rambursarea: butonul apare o dată pe comandă, cere confirmare, apoi reîncarcă cifrele', () => {
    open([
      attendee({ ticketId: 1, orderId: 7 }),
      attendee({ ticketId: 2, orderId: 7, shortCode: 'BB' }),
      attendee({ ticketId: 3, orderId: 8 }),
    ]);

    const buttons = el().querySelectorAll('.link-button');
    expect(buttons.length).toBe(2); // comenzile 7 și 8, nu câte unul pe bilet

    (buttons[0] as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(text()).toContain('Rambursezi comanda #7 (2 bilete)?');
    http.expectNone('/api/manage/orders/7/refund');

    button('Da')!.click();
    const refund = http.expectOne('/api/manage/orders/7/refund');
    expect(refund.request.method).toBe('POST');
    refund.flush({ orderId: 7, status: 'Refunded' });
    fixture.detectChanges();

    expect(text()).toContain('Comanda #7 a fost rambursată');
    http.expectOne('/api/organizer/events/10/sales'); // cifrele se reîncarcă
    expect(attendeeRequests().length).toBe(1);
  });

  it('„Nu” anulează confirmarea fără să trimită nimic', () => {
    open([attendee({ orderId: 7 })]);
    (el().querySelector('.link-button') as HTMLButtonElement).click();
    fixture.detectChanges();

    button('Nu')!.click();
    fixture.detectChanges();

    expect(text()).not.toContain('Rambursezi');
    http.expectNone('/api/manage/orders/7/refund');
  });

  it('un refuz de la server (ex. bilet scanat între timp) apare ca mesaj', () => {
    open([attendee({ orderId: 7 })]);
    (el().querySelector('.link-button') as HTMLButtonElement).click();
    fixture.detectChanges();
    button('Da')!.click();

    http
      .expectOne('/api/manage/orders/7/refund')
      .flush(
        { message: 'Un bilet din comandă a fost deja folosit la intrare.' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(el().querySelector('.form-alert')?.textContent).toContain('deja folosit');
  });

  it('evenimentul altcuiva (404) arată un mesaj clar', () => {
    TestBed.configureTestingModule({ imports: [OrganizerEvent], providers: providers() });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OrganizerEvent);
    fixture.componentRef.setInput('id', '99');
    fixture.detectChanges();

    http
      .expectOne('/api/organizer/events/99/sales')
      .flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(el().querySelector('[role=alert]')?.textContent).toContain('nu este al tău');
  });
});
