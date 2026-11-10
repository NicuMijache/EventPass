import { Component, DestroyRef, ElementRef, inject, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { CameraError, toCameraError } from '../../core/qr/camera.service';
import { ScannerEngine, ScannerSession } from '../../core/qr/scanner-engine';
import { parseScannedCode, parseTicketCode, shortCodeOf } from '../../core/qr/ticket-code';

// Cum s-a ajuns la un cod: cu camera sau tastat de mână
export type ScanSource = 'camera' | 'manual';

export interface ScanEntry {
  id: number;
  at: Date;
  source: ScanSource;
  // ticket = un token EventPass întreg; short = doar codul scurt (tastat); unknown = altceva
  outcome: 'ticket' | 'short' | 'unknown';
  // Pentru ticket: tokenul; pentru short: cele 8 caractere; pentru unknown: textul citit (scurtat)
  value: string;
}

type Phase = 'idle' | 'starting' | 'scanning';

// Cât din textul unui cod străin afișăm (un QR poate conține mii de caractere)
const MAX_UNKNOWN_LENGTH = 80;
const HISTORY_SIZE = 5;

@Component({
  selector: 'app-scan',
  imports: [ReactiveFormsModule, DatePipe],
  templateUrl: './scan.html',
  styleUrl: './scan.scss',
})
export class Scan {
  private readonly engine = inject(ScannerEngine);

  private readonly video = viewChild.required<ElementRef<HTMLVideoElement>>('video');

  protected readonly phase = signal<Phase>('idle');
  protected readonly error = signal<string | null>(null);
  protected readonly last = signal<ScanEntry | null>(null);
  protected readonly history = signal<ScanEntry[]>([]);

  protected readonly manualCode = new FormControl('', {
    nonNullable: true,
    validators: [Validators.required, (control) => (parseTicketCode(control.value) ? null : { ticketCode: true })],
  });
  protected readonly shortCodeOf = shortCodeOf;

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
      parsed.kind === 'token' ? parsed.token : parsed.code
    );
    this.manualCode.reset('');
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
        }
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
      this.record('camera', 'unknown', text.length > MAX_UNKNOWN_LENGTH ? `${text.slice(0, MAX_UNKNOWN_LENGTH)}…` : text);
    }
  }

  private record(source: ScanSource, outcome: ScanEntry['outcome'], value: string): void {
    const entry: ScanEntry = { id: this.nextId++, at: new Date(), source, outcome, value };
    this.last.set(entry);
    this.history.update((list) => [entry, ...list].slice(0, HISTORY_SIZE));
    // Vibrație scurtă la un bilet, lungă la un cod străin (pe telefoanele care o permit)
    globalThis.navigator?.vibrate?.(outcome === 'unknown' ? 250 : 60);
  }
}
