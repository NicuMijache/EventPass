import {
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { CameraError, toCameraError } from '../../core/qr/camera.service';
import { ScannerEngine, ScannerSession } from '../../core/qr/scanner-engine';
import { parseScannedCode, parseTicketCode, shortCodeOf } from '../../core/qr/ticket-code';
import { TicketsService } from '../../core/services/tickets.service';
import { TicketValidation } from '../../core/models/ticket.models';
import { errorMessage } from '../../core/http-errors';

// Cum s-a ajuns la un cod: cu camera sau tastat de mână
export type ScanSource = 'camera' | 'manual';

// Verificarea pe server a unui cod: în curs, terminată (cu răspunsul) sau eșuată (server oprit, fără internet...)
export type ScanCheck =
  | { state: 'pending' }
  | { state: 'done'; validation: TicketValidation }
  | { state: 'failed'; message: string };

// Cum arată rezultatul: verde = intră, galben = deja folosit, roșu = nu intră
export type ScanTone = 'ok' | 'warn' | 'bad' | 'pending';

export interface ScanEntry {
  id: number;
  at: Date;
  source: ScanSource;
  // ticket = un token EventPass întreg; short = doar codul scurt (tastat); unknown = altceva
  outcome: 'ticket' | 'short' | 'unknown';
  // Pentru ticket: tokenul; pentru short: cele 8 caractere; pentru unknown: textul citit (scurtat)
  value: string;
  // null pentru codurile nerecunoscute: pe acelea nu le trimitem serverului
  check: ScanCheck | null;
}

type Phase = 'idle' | 'starting' | 'scanning';

// Cât din textul unui cod străin afișăm (un QR poate conține mii de caractere)
const MAX_UNKNOWN_LENGTH = 80;
const HISTORY_SIZE = 5;

// Vibrații: una scurtă = intră; trei = oprește-te și uită-te la ecran
const VIBRATE_OK = 80;
const VIBRATE_STOP = [200, 100, 200, 100, 200];

export function toneOf(entry: ScanEntry): ScanTone {
  if (entry.check === null) return 'bad';
  switch (entry.check.state) {
    case 'pending':
      return 'pending';
    case 'failed':
      return 'bad';
    case 'done':
      return entry.check.validation.result === 'Valid'
        ? 'ok'
        : entry.check.validation.result === 'AlreadyUsed'
          ? 'warn'
          : 'bad';
  }
}

@Component({
  selector: 'app-scan',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './scan.html',
  styleUrl: './scan.scss',
})
export class Scan {
  private readonly engine = inject(ScannerEngine);
  private readonly tickets = inject(TicketsService);

  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');

  protected readonly phase = signal<Phase>('idle');
  protected readonly error = signal<string | null>(null);
  protected readonly history = signal<ScanEntry[]>([]);
  // Ultimul cod citit = primul din istoric
  protected readonly last = computed(() => this.history()[0] ?? null);

  protected readonly manualCode = new FormControl('', {
    nonNullable: true,
    validators: [
      Validators.required,
      (control) => (parseTicketCode(control.value) ? null : { ticketCode: true }),
    ],
  });
  protected readonly shortCodeOf = shortCodeOf;
  protected readonly toneOf = toneOf;

  private session: ScannerSession | null = null;
  private nextId = 1;
  private destroyed = false;

  constructor() {
    // Oprește camera la părăsirea paginii (altfel lumina ei rămâne aprinsă)
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.stopSession();
    });
  }

  protected toggleCamera(): void {
    if (this.phase() === 'scanning') {
      this.stopCamera();
    } else if (this.phase() === 'idle') {
      void this.startCamera();
    }
  }

  protected submitManual(event: Event): void {
    // Fără asta, browserul trimite formularul și reîncarcă pagina
    event.preventDefault();

    if (this.manualCode.invalid) {
      this.manualCode.markAsTouched();
      return;
    }
    const parsed = parseTicketCode(this.manualCode.value);
    if (!parsed) return;

    this.record(
      'manual',
      parsed.kind === 'token' ? 'ticket' : 'short',
      parsed.kind === 'token' ? parsed.token : parsed.code,
    );
    this.manualCode.reset('');
  }

  // După o eroare de rețea, aceeași verificare se poate cere din nou
  protected retry(entry: ScanEntry): void {
    if (entry.check?.state === 'failed') this.check(entry.id, entry.value);
  }

  // Textul scurt din istoric
  protected verdictOf(entry: ScanEntry): string {
    const check = entry.check;
    if (check === null) return 'nerecunoscut';
    if (check.state === 'pending') return 'se verifică…';
    if (check.state === 'failed') return 'neverificat';
    switch (check.validation.result) {
      case 'Valid':
        return 'valid';
      case 'AlreadyUsed':
        return 'deja folosit';
      default:
        return 'respins';
    }
  }

  private async startCamera(): Promise<void> {
    this.phase.set('starting');
    this.error.set(null);

    try {
      const session = await this.engine.start(
        this.video().nativeElement,
        (text) => this.onCameraCode(text),
        () => {
          this.session = null;
          this.phase.set('idle');
          this.error.set('Camera s-a oprit. Apasă „Pornește camera” ca să continui.');
        },
      );

      // Cât timp s-a pornit camera, utilizatorul a plecat din pagină: o oprim imediat
      if (this.destroyed) {
        session.stop();
        return;
      }
      this.session = session;
      this.phase.set('scanning');
    } catch (error) {
      if (!this.destroyed) {
        this.phase.set('idle');
        this.error.set(error instanceof CameraError ? error.message : toCameraError(error).message);
      }
    }
  }

  private stopCamera(): void {
    this.stopSession();
    this.phase.set('idle');
  }

  private stopSession(): void {
    this.session?.stop();
    this.session = null;
  }

  private onCameraCode(text: string): void {
    const parsed = parseScannedCode(text);
    if (parsed) {
      this.record('camera', 'ticket', parsed.token);
    } else {
      // Un cod care nu e un bilet EventPass (link, cod de produs...): îl arătăm scurtat, ca text, niciodată ca link
      this.record(
        'camera',
        'unknown',
        text.length > MAX_UNKNOWN_LENGTH ? `${text.slice(0, MAX_UNKNOWN_LENGTH)}…` : text,
      );
    }
  }

  private record(source: ScanSource, outcome: ScanEntry['outcome'], value: string): void {
    const id = this.nextId++;
    const entry: ScanEntry = {
      id,
      at: new Date(),
      source,
      outcome,
      value,
      check: outcome === 'unknown' ? null : { state: 'pending' },
    };
    this.history.update((list) => [entry, ...list].slice(0, HISTORY_SIZE));

    if (outcome === 'unknown') {
      this.vibrate(VIBRATE_STOP);
    } else {
      this.check(id, value);
    }
  }

  // Trimite codul serverului. Serverul hotărăște (și marchează biletul ca folosit), nu browserul:
  // aici nu știm dacă un bilet a mai fost scanat de alt angajat, la altă intrare.
  private check(id: number, code: string): void {
    this.setCheck(id, { state: 'pending' });
    this.tickets.validate(code).subscribe({
      next: (validation) => {
        this.setCheck(id, { state: 'done', validation });
        this.vibrate(validation.result === 'Valid' ? VIBRATE_OK : VIBRATE_STOP);
      },
      error: (err) => {
        this.setCheck(id, {
          state: 'failed',
          message: errorMessage(err, 'Nu am putut verifica biletul. Încearcă din nou.'),
        });
        this.vibrate(VIBRATE_STOP);
      },
    });
  }

  // Răspunsul poate veni după ce au mai fost scanate alte coduri: actualizăm exact intrarea lui
  private setCheck(id: number, check: ScanCheck): void {
    this.history.update((list) =>
      list.map((entry) => (entry.id === id ? { ...entry, check } : entry)),
    );
  }

  private vibrate(pattern: number | number[]): void {
    // Doar pe telefoanele care o permit; după părăsirea paginii, nu mai vibrăm
    if (!this.destroyed) globalThis.navigator?.vibrate?.(pattern);
  }
}
