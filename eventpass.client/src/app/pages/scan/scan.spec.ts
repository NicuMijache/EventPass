import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TicketValidation } from '../../core/models/ticket.models';
import { CameraError } from '../../core/qr/camera.service';
import { ScannerEngine, ScannerSession } from '../../core/qr/scanner-engine';
import { Scan } from './scan';

const TOKEN = '3F2A9C1B-7D4E-4A2B-9C81-5E6F7A8B9C0D';

// Motorul de scanare fals: în loc de cameră, testul „citește” coduri când vrea el
class FakeEngine {
  onCode: (text: string) => void = () => undefined;
  onEnded: () => void = () => undefined;
  stop = vi.fn();
  start = vi.fn(
    async (
      _video: HTMLVideoElement,
      onCode: (text: string) => void,
      onEnded?: () => void,
    ): Promise<ScannerSession> => {
      this.onCode = onCode;
      this.onEnded = onEnded ?? (() => undefined);
      return { stop: this.stop };
    },
  );
}

describe('Scan (pagina de scanare)', () => {
  let fixture: ComponentFixture<Scan>;
  let engine: FakeEngine;
  let http: HttpTestingController;
  let vibrate: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    engine = new FakeEngine();
    vibrate = vi.fn(() => true);
    Object.defineProperty(navigator, 'vibrate', { value: vibrate, configurable: true });
    TestBed.configureTestingModule({
      imports: [Scan],
      providers: [
        { provide: ScannerEngine, useValue: engine },
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Scan);
    fixture.detectChanges();
  });

  afterEach(() => {
    delete (navigator as { vibrate?: unknown }).vibrate;
  });

  const empty: TicketValidation = {
    result: 'Valid',
    message: '',
    shortCode: null,
    eventTitle: null,
    eventStartsAt: null,
    ticketTypeName: null,
    holderName: null,
    usedAt: null,
  };

  // Răspunsul serverului pentru cererea de validare în așteptare (cu detaliile unui bilet găsit)
  function answer(result: TicketValidation['result'], extra: Partial<TicketValidation> = {}) {
    const found = result === 'Valid' || result === 'AlreadyUsed' || result === 'NotPaid';
    const body: TicketValidation = {
      result,
      message: `mesaj ${result}`,
      shortCode: found ? '3F2A9C1B' : null,
      eventTitle: found ? 'Concert Rock' : null,
      eventStartsAt: found ? '2026-12-01T18:00:00Z' : null,
      ticketTypeName: found ? 'VIP' : null,
      holderName: found ? 'Ana Pop' : null,
      usedAt: result === 'Valid' || result === 'AlreadyUsed' ? '2026-11-10T17:05:09Z' : null,
      ...extra,
    };
    http.expectOne('/api/tickets/validate').flush(body);
    fixture.detectChanges();
  }

  const resultBox = () => el().querySelector('.result') as HTMLElement;

  const el = () => fixture.nativeElement as HTMLElement;
  const text = () => (el().textContent ?? '').replace(/\s+/g, ' ');
  const cameraButton = () => el().querySelector('.camera .btn') as HTMLButtonElement;

  async function startCamera() {
    cameraButton().click();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function scan(code: string) {
    engine.onCode(code);
    fixture.detectChanges();
  }

  // Întoarce evenimentul „submit”, ca testele să poată verifica dacă s-a oprit trimiterea nativă a formularului
  function typeManual(value: string): Event {
    const input = el().querySelector('.manual__input') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    const submit = new Event('submit', { cancelable: true });
    (el().querySelector('.manual') as HTMLFormElement).dispatchEvent(submit);
    fixture.detectChanges();
    return submit;
  }

  it('la început camera e oprită și nu s-a citit nimic', () => {
    expect(text()).toContain('Camera este oprită');
    expect(text()).toContain('Niciun cod citit încă');
    expect(cameraButton().textContent).toContain('Pornește camera');
    expect(engine.start).not.toHaveBeenCalled();
  });

  it('camera pornește DOAR când apeși butonul (nu singură, la deschiderea paginii)', async () => {
    expect(engine.start).not.toHaveBeenCalled();

    await startCamera();

    expect(engine.start).toHaveBeenCalledTimes(1);
    expect(text()).toContain('Caut un cod QR');
    expect(cameraButton().textContent).toContain('Oprește camera');
  });

  it('îi dă motorului elementul <video> al paginii', async () => {
    await startCamera();

    expect(engine.start.mock.calls[0][0]).toBeInstanceOf(HTMLVideoElement);
  });

  it('un cod de bilet citit cu camera apare cu codul scurt și tokenul întreg', async () => {
    await startCamera();

    scan(TOKEN);

    expect(text()).toContain('Se verifică');
    expect(text()).toContain('3F2A9C1B');
    expect(text()).toContain(TOKEN);
    expect(text()).toContain('cu camera');
    expect(http.expectOne('/api/tickets/validate').request.body).toEqual({ code: TOKEN });
  });

  it('un cod citit cu litere mici e normalizat (majuscule) înainte de afișare', async () => {
    await startCamera();

    scan(TOKEN.toLowerCase());

    expect(text()).toContain(TOKEN);
    expect(http.expectOne('/api/tickets/validate').request.body).toEqual({ code: TOKEN });
  });

  it('un cod care nu e bilet (un link) e marcat „nerecunoscut” și afișat ca TEXT, nu ca link', async () => {
    await startCamera();

    scan('https://site-rau.example/pagina');
    const shown = el().querySelector('.result__token');

    expect(text()).toContain('Cod nerecunoscut');
    expect(text()).toContain('Nu este un bilet EventPass');
    expect(shown?.textContent).toContain('https://site-rau.example/pagina');
    expect(el().querySelector('.result a')).toBeNull();
    http.expectNone('/api/tickets/validate'); // nu întrebăm serverul despre coduri străine
    expect(resultBox().classList).toContain('result--bad');
  });

  it('cod HTML din QR e afișat ca text, nu interpretat', async () => {
    await startCamera();

    scan('<img src=x onerror=alert(1)><b>bold</b>');

    expect(el().querySelector('.result img')).toBeNull();
    expect(el().querySelector('.result b')).toBeNull();
    expect(text()).toContain('<img src=x onerror=alert(1)>');
  });

  it('un cod străin foarte lung e scurtat', async () => {
    await startCamera();

    scan('A'.repeat(5000));
    const shown = el().querySelector('.result__token')?.textContent ?? '';

    expect(shown.length).toBeLessThan(120);
    expect(shown).toContain('…');
  });

  it('un cod scurt (8 caractere) citit cu camera NU e acceptat ca bilet', async () => {
    await startCamera();

    scan('FA46C34B');

    expect(text()).toContain('Cod nerecunoscut');
    http.expectNone('/api/tickets/validate');
  });

  it('„Oprește camera” oprește motorul și revine la „Pornește camera”', async () => {
    await startCamera();

    cameraButton().click();
    fixture.detectChanges();

    expect(engine.stop).toHaveBeenCalledTimes(1);
    expect(cameraButton().textContent).toContain('Pornește camera');
    expect(text()).toContain('Camera este oprită');
  });

  it('se poate porni din nou după oprire', async () => {
    await startCamera();
    cameraButton().click();
    fixture.detectChanges();

    await startCamera();

    expect(engine.start).toHaveBeenCalledTimes(2);
  });

  it('un dublu-click pe „Pornește” nu pornește camera de două ori', async () => {
    cameraButton().click();
    cameraButton().click();
    await fixture.whenStable();

    expect(engine.start).toHaveBeenCalledTimes(1);
  });

  describe('erori de cameră', () => {
    it.each([
      ['denied', 'Accesul la cameră a fost refuzat'],
      ['not-found', 'Nu am găsit nicio cameră'],
      ['insecure', 'HTTPS'],
    ] as const)('%s → arată mesajul și permite reîncercarea', async (kind, expected) => {
      engine.start.mockRejectedValueOnce(new CameraError(kind, expected));

      await startCamera();

      expect(el().querySelector('[role=alert]')?.textContent).toContain(expected);
      expect(cameraButton().textContent).toContain('Pornește camera');
      expect(cameraButton().disabled).toBe(false);
    });

    it('o eroare neașteptată primește un mesaj prietenos, nu textul tehnic', async () => {
      engine.start.mockRejectedValueOnce(new Error('Cannot read properties of undefined'));

      await startCamera();

      const message = el().querySelector('[role=alert]')?.textContent ?? '';
      expect(message).not.toContain('Cannot read');
      expect(message).toContain('Nu am putut porni camera');
    });

    it('mesajul de eroare dispare când reîncerci cu succes', async () => {
      engine.start.mockRejectedValueOnce(new CameraError('denied', 'Accesul refuzat'));
      await startCamera();
      expect(el().querySelector('[role=alert]')).not.toBeNull();

      await startCamera();

      expect(el().querySelector('[role=alert]')).toBeNull();
    });

    it('dacă după un cod citit camera se oprește singură, afișează un mesaj și lasă codul citit', async () => {
      await startCamera();
      scan(TOKEN);

      engine.onEnded();
      fixture.detectChanges();

      expect(el().querySelector('[role=alert]')?.textContent).toContain('Camera s-a oprit');
      expect(cameraButton().textContent).toContain('Pornește camera');
      expect(text()).toContain(TOKEN);
    });
  });

  describe('introducere de mână', () => {
    it('trimiterea formularului NU reîncarcă pagina (altfel s-ar opri camera)', () => {
      expect(typeManual(TOKEN).defaultPrevented).toBe(true);
      expect(typeManual('cod-invalid').defaultPrevented).toBe(true);
    });

    it('acceptă tokenul întreg', () => {
      typeManual(TOKEN);

      expect(text()).toContain('de mână');
      expect(text()).toContain(TOKEN);
      expect(http.expectOne('/api/tickets/validate').request.body).toEqual({ code: TOKEN });
    });

    it('acceptă codul scurt, cu litere mici și spațiu', () => {
      typeManual('fa46 c34b');

      expect(text()).toContain('FA46C34B');
      expect(http.expectOne('/api/tickets/validate').request.body).toEqual({ code: 'FA46C34B' });
    });

    it('goleste câmpul după un cod acceptat', () => {
      typeManual(TOKEN);

      expect((el().querySelector('.manual__input') as HTMLInputElement).value).toBe('');
    });

    it.each(['', 'abc', 'FA46C34G', 'https://example.com', '12345678901'])(
      'respinge %j cu un mesaj',
      (value) => {
        typeManual(value);

        expect(text()).toContain('Cod invalid');
        expect(text()).toContain('Niciun cod citit încă');
        http.expectNone('/api/tickets/validate');
      },
    );

    it('un cod invalid nu strică ultimul cod citit', async () => {
      await startCamera();
      scan(TOKEN);

      typeManual('nu-e-cod');

      expect(text()).toContain(TOKEN);
    });
  });

  describe('verificarea pe server', () => {
    it('bilet valid → verde, „Poate intra”, cu detaliile biletului și o vibrație scurtă', async () => {
      await startCamera();
      scan(TOKEN);

      answer('Valid');

      expect(text()).toContain('Bilet valid');
      expect(text()).toContain('Poate intra');
      expect(text()).toContain('Concert Rock');
      expect(text()).toContain('VIP');
      expect(text()).toContain('Ana Pop');
      expect(resultBox().classList).toContain('result--ok');
      expect(vibrate).toHaveBeenLastCalledWith(80);
    });

    it('bilet deja folosit → galben, cu ora primei scanări și o vibrație lungă', async () => {
      await startCamera();
      scan(TOKEN);

      answer('AlreadyUsed');

      expect(text()).toContain('Deja utilizat');
      expect(text()).toContain('Nu poate intra din nou');
      expect(el().querySelector('.result__message')?.textContent).toMatch(/\d\d:05:09/);
      expect(resultBox().classList).toContain('result--warn');
      expect(vibrate.mock.lastCall?.[0]).toEqual([200, 100, 200, 100, 200]);
    });

    it.each(['NotFound', 'NotPaid', 'OtherOrganizer', 'Ambiguous', 'InvalidCode'] as const)(
      '%s → roșu, „Respins”, cu mesajul serverului',
      (result) => {
        typeManual('FA46C34B');

        answer(result, { message: `Motivul pentru ${result}` });

        expect(text()).toContain('Respins');
        expect(text()).toContain(`Motivul pentru ${result}`);
        expect(text()).not.toContain('Poate intra');
        expect(resultBox().classList).toContain('result--bad');
      },
    );

    it('cât așteaptă răspunsul, NU arată nici valid, nici respins', async () => {
      await startCamera();
      scan(TOKEN);

      expect(text()).toContain('Se verifică');
      expect(text()).not.toContain('Bilet valid');
      expect(text()).not.toContain('Respins');
      expect(resultBox().classList).toContain('result--pending');
      http.expectOne('/api/tickets/validate');
    });

    it('serverul nu răspunde → „Neverificat”, iar „Verifică din nou” retrimite același cod', async () => {
      await startCamera();
      scan(TOKEN);

      http.expectOne('/api/tickets/validate').error(new ProgressEvent('error'), { status: 0 });
      fixture.detectChanges();

      expect(text()).toContain('Neverificat');
      expect(text()).toContain('Serverul nu răspunde');
      expect(text()).not.toContain('Poate intra');
      expect(resultBox().classList).toContain('result--bad');

      (el().querySelector('.result__retry') as HTMLButtonElement).click();
      fixture.detectChanges();
      expect(text()).toContain('Se verifică');
      expect(http.expectOne('/api/tickets/validate').request.body).toEqual({ code: TOKEN });
    });

    it('un răspuns de eroare cu mesaj (ex. 403) afișează mesajul serverului', () => {
      typeManual('FA46C34B');

      http
        .expectOne('/api/tickets/validate')
        .flush({ message: 'Nu ai acces.' }, { status: 403, statusText: 'Forbidden' });
      fixture.detectChanges();

      expect(text()).toContain('Nu ai acces.');
    });

    it('răspunsurile care vin în altă ordine ajung fiecare la codul lui', async () => {
      await startCamera();
      scan(TOKEN);
      scan('AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE');
      const [first, second] = http.match('/api/tickets/validate');

      // Al doilea răspunde primul: valid. Apoi primul: deja folosit.
      second.flush({ ...empty, result: 'Valid' });
      first.flush({ ...empty, result: 'AlreadyUsed', usedAt: '2026-11-10T17:00:00Z' });
      fixture.detectChanges();

      // Ultimul cod citit rămâne cel afișat mare, cu verdictul LUI
      expect(el().querySelector('.result__code')?.textContent).toContain('AAAAAAAA');
      expect(text()).toContain('Bilet valid');
      const rows = Array.from(el().querySelectorAll('.history__item')).map((n) => [
        n.querySelector('.history__code')?.textContent?.trim(),
        n.querySelector('.history__verdict')?.textContent?.trim(),
      ]);
      expect(rows).toEqual([
        ['AAAAAAAA', 'valid'],
        ['3F2A9C1B', 'deja folosit'],
      ]);
    });

    it('după părăsirea paginii, un răspuns întârziat nu mai face telefonul să vibreze', async () => {
      await startCamera();
      scan(TOKEN);
      const pending = http.expectOne('/api/tickets/validate');
      vibrate.mockClear();

      fixture.destroy();
      pending.flush({ ...empty, result: 'Valid' });

      expect(vibrate).not.toHaveBeenCalled();
    });
  });

  describe('istoric', () => {
    it('se afișează abia de la al doilea cod, cu cel mai nou primul', async () => {
      await startCamera();
      scan(TOKEN);
      expect(el().querySelector('.history')).toBeNull();

      scan('AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE');
      const items = Array.from(el().querySelectorAll('.history__code')).map((n) =>
        n.textContent?.trim(),
      );

      expect(items).toEqual(['AAAAAAAA', '3F2A9C1B']);
    });

    it('păstrează doar ultimele 5 coduri', async () => {
      await startCamera();
      for (let i = 1; i <= 8; i++) scan(`0000000${i}-0000-0000-0000-000000000000`);

      const items = Array.from(el().querySelectorAll('.history__code')).map((n) =>
        n.textContent?.trim(),
      );

      expect(items).toEqual(['00000008', '00000007', '00000006', '00000005', '00000004']);
    });
  });

  describe('curățenie', () => {
    it('părăsirea paginii oprește camera', async () => {
      await startCamera();

      fixture.destroy();

      expect(engine.stop).toHaveBeenCalledTimes(1);
    });

    it('părăsirea paginii cât camera încă pornește o oprește imediat ce e gata (altfel rămâne aprinsă)', async () => {
      let finish!: (session: ScannerSession) => void;
      engine.start.mockImplementationOnce(
        () => new Promise<ScannerSession>((resolve) => (finish = resolve)),
      );
      cameraButton().click();

      fixture.destroy();
      finish({ stop: engine.stop });
      await Promise.resolve();

      expect(engine.stop).toHaveBeenCalledTimes(1);
    });

    it('părăsirea paginii cu camera oprită nu face nimic în plus', () => {
      fixture.destroy();

      expect(engine.stop).not.toHaveBeenCalled();
    });
  });
});
