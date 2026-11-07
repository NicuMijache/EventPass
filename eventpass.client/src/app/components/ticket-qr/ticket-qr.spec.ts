import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TicketQr } from './ticket-qr';

describe('TicketQr', () => {
  let fixture: ComponentFixture<TicketQr>;
  let http: HttpTestingController;
  const originalCreate = URL.createObjectURL;
  const originalRevoke = URL.revokeObjectURL;
  let revoked: string[];

  beforeEach(() => {
    // jsdom (mediul de test) nu are URL.createObjectURL: îl înlocuim cu variante simple
    revoked = [];
    let n = 0;
    URL.createObjectURL = () => `blob:test-${++n}`;
    URL.revokeObjectURL = (url: string) => void revoked.push(url);

    TestBed.configureTestingModule({
      imports: [TicketQr],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TicketQr);
    fixture.componentRef.setInput('orderId', 5);
    fixture.componentRef.setInput('ticketId', 7);
    fixture.detectChanges();
  });

  afterEach(() => {
    http.verify();
    URL.createObjectURL = originalCreate;
    URL.revokeObjectURL = originalRevoke;
  });

  const element = () => fixture.nativeElement as HTMLElement;

  it('cere imaginea QR a biletului potrivit, ca blob', () => {
    const request = http.expectOne('/api/orders/5/tickets/7/qr');

    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['png'], { type: 'image/png' }));
  });

  it('cât se încarcă, arată o stare de așteptare, nu o imagine', () => {
    expect(element().querySelector('[role=status]')).toBeTruthy();
    expect(element().querySelector('img')).toBeNull();
    http.expectOne('/api/orders/5/tickets/7/qr').flush(new Blob());
  });

  it('după încărcare arată imaginea și link-ul de descărcare', () => {
    http.expectOne('/api/orders/5/tickets/7/qr').flush(new Blob(['png'], { type: 'image/png' }));
    fixture.detectChanges();

    expect(element().querySelector('img')?.getAttribute('src')).toBe('blob:test-1');
    const download = element().querySelector('a.qr__download');
    expect(download?.getAttribute('download')).toBe('bilet-5-7.png');
  });

  it('dacă serverul refuză, arată un mesaj și „Încearcă din nou” cere imaginea încă o dată', () => {
    http.expectOne('/api/orders/5/tickets/7/qr').error(new ProgressEvent('error'), { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(element().textContent).toContain('Codul nu s-a încărcat');
    (element().querySelector('.qr__retry') as HTMLButtonElement).click();
    fixture.detectChanges();

    http.expectOne('/api/orders/5/tickets/7/qr').flush(new Blob(['png']));
  });

  it('eliberează adresa blob când componenta dispare (altfel ar ocupa memorie)', () => {
    http.expectOne('/api/orders/5/tickets/7/qr').flush(new Blob(['png']));
    fixture.detectChanges();

    fixture.destroy();

    expect(revoked).toContain('blob:test-1');
  });
});
