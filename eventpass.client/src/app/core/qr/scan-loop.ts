import { FrameData, QrReader } from './qr-reader';

// Un cadru luat de la cameră, plus (opțional) aceeași imagine într-o formă pentru detectorul nativ
export interface GrabbedFrame {
  frame: FrameData;
  source?: CanvasImageSource;
}

export interface ScanLoopOptions {
  // La câte milisecunde se ia un cadru nou
  intervalMs: number;
  // Cât timp trebuie să lipsească un cod din imagine ca să poată fi „citit” din nou.
  // Cât timp îl ții continuu în fața camerei, se raportează O SINGURĂ DATĂ.
  absenceMs: number;
}

// Ia cadre de la cameră la intervale regulate, caută un cod în ele și anunță când a citit unul.
// Logica asta nu atinge camera sau pagina, ca să poată fi testată cu cadre false și un ceas fals.
export class ScanLoop {
  private timer: ReturnType<typeof setInterval> | null = null;
  private busy = false;
  private stopped = false;
  // Ultimul cod văzut și când l-am văzut ultima dată
  private lastText: string | null = null;
  private lastSeenAt = 0;

  constructor(
    private readonly grab: () => GrabbedFrame | null,
    private readonly reader: Pick<QrReader, 'read'>,
    private readonly onCode: (text: string) => void,
    private readonly options: ScanLoopOptions,
    private readonly now: () => number = () => Date.now()
  ) {}

  start(): void {
    if (this.timer || this.stopped) return;
    this.timer = setInterval(() => void this.tick(), this.options.intervalMs);
  }

  stop(): void {
    this.stopped = true;
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
  }

  // Un pas al buclei (public, ca testele să-l poată apela direct)
  async tick(): Promise<void> {
    // Citirea unui cadru poate dura mai mult decât intervalul: nu pornim una nouă peste cea veche
    if (this.busy || this.stopped) return;
    this.busy = true;
    try {
      const grabbed = this.grab();
      if (!grabbed) return;

      const text = await this.reader.read(grabbed.frame, grabbed.source);
      // Între timp s-ar fi putut opri scannerul: nu mai anunțăm nimic
      if (!text || this.stopped) return;

      const now = this.now();
      const sameAsBefore = text === this.lastText && now - this.lastSeenAt < this.options.absenceMs;
      this.lastText = text;
      this.lastSeenAt = now;
      if (!sameAsBefore) {
        this.onCode(text);
      }
    } catch {
      // O eroare la un cadru (cadru stricat, canvas nepregătit) nu trebuie să oprească scannerul;
      // încercăm din nou la următorul
    } finally {
      this.busy = false;
    }
  }
}
