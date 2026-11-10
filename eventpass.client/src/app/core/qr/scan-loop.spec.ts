import { FrameData } from './qr-reader';
import { GrabbedFrame, ScanLoop } from './scan-loop';

const FRAME: FrameData = { data: new Uint8ClampedArray(4), width: 1, height: 1 };
const OPTIONS = { intervalMs: 100, absenceMs: 1500 };

// Un „cititor” care întoarce pe rând ce i se dă (undefined/null = nu vede niciun cod)
function scriptedReader(...results: (string | null)[]) {
  let i = 0;
  return {
    read: vi.fn(async () => (i < results.length ? results[i++] : null)),
  };
}

describe('ScanLoop', () => {
  let now: number;
  const clock = () => now;
  const grab = (): GrabbedFrame => ({ frame: FRAME });

  beforeEach(() => {
    now = 10_000;
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function create(reader: { read: ReturnType<typeof vi.fn> }, onCode = vi.fn(), grabber: () => GrabbedFrame | null = grab) {
    return { loop: new ScanLoop(grabber, reader as never, onCode, OPTIONS, clock), onCode };
  }

  it('anunță un cod citit', async () => {
    const { loop, onCode } = create(scriptedReader('COD-1'));

    await loop.tick();

    expect(onCode).toHaveBeenCalledExactlyOnceWith('COD-1');
  });

  it('nu anunță nimic când nu vede niciun cod', async () => {
    const { loop, onCode } = create(scriptedReader(null, null));

    await loop.tick();
    await loop.tick();

    expect(onCode).not.toHaveBeenCalled();
  });

  it('ține codul în fața camerei: se anunță O SINGURĂ DATĂ, nu la fiecare cadru', async () => {
    const { loop, onCode } = create(scriptedReader('COD-1', 'COD-1', 'COD-1', 'COD-1', 'COD-1'));

    for (let i = 0; i < 5; i++) {
      await loop.tick();
      now += 100; // cadre la 100 ms: codul rămâne vizibil ~0,5 s
    }

    expect(onCode).toHaveBeenCalledTimes(1);
  });

  it('chiar dacă îl ții foarte mult (peste pauza de absență), tot o singură dată cât e continuu vizibil', async () => {
    const { loop, onCode } = create(scriptedReader(...Array(60).fill('COD-1')));

    for (let i = 0; i < 60; i++) {
      await loop.tick();
      now += 100; // 6 secunde de prezență continuă
    }

    expect(onCode).toHaveBeenCalledTimes(1);
  });

  it('un cod DIFERIT se anunță imediat, fără să aștepte', async () => {
    const { loop, onCode } = create(scriptedReader('COD-1', 'COD-2'));

    await loop.tick();
    now += 100;
    await loop.tick();

    expect(onCode.mock.calls.map((c) => c[0])).toEqual(['COD-1', 'COD-2']);
  });

  it('același cod, dar după ce a lipsit mai mult decât pauza: se anunță din nou (alt scan intenționat)', async () => {
    const { loop, onCode } = create(scriptedReader('COD-1', 'COD-1'));

    await loop.tick();
    now += OPTIONS.absenceMs + 1; // a fost dat la o parte, apoi arătat din nou
    await loop.tick();

    expect(onCode).toHaveBeenCalledTimes(2);
  });

  it('același cod după o pauză mai scurtă decât cea de absență: NU se anunță din nou', async () => {
    const { loop, onCode } = create(scriptedReader('COD-1', 'COD-1'));

    await loop.tick();
    now += OPTIONS.absenceMs - 1;
    await loop.tick();

    expect(onCode).toHaveBeenCalledTimes(1);
  });

  it('alternarea între două coduri: fiecare schimbare se anunță', async () => {
    const { loop, onCode } = create(scriptedReader('A', 'B', 'A', 'B'));

    for (let i = 0; i < 4; i++) {
      await loop.tick();
      now += 100;
    }

    expect(onCode.mock.calls.map((c) => c[0])).toEqual(['A', 'B', 'A', 'B']);
  });

  it('sare peste cadrele indisponibile (camera încă nu are imagine)', async () => {
    const reader = scriptedReader('COD-1');
    const { loop, onCode } = create(reader, vi.fn(), () => null);

    await loop.tick();

    expect(reader.read).not.toHaveBeenCalled();
    expect(onCode).not.toHaveBeenCalled();
  });

  it('o eroare la citire nu oprește scannerul: următorul cadru merge', async () => {
    const reader = {
      read: vi.fn().mockRejectedValueOnce(new Error('cadru stricat')).mockResolvedValueOnce('COD-1'),
    };
    const { loop, onCode } = create(reader);

    await loop.tick();
    await loop.tick();

    expect(onCode).toHaveBeenCalledExactlyOnceWith('COD-1');
  });

  it('o eroare la luarea cadrului nu oprește scannerul', async () => {
    let first = true;
    const { loop, onCode } = create(scriptedReader('COD-1'), vi.fn(), () => {
      if (first) {
        first = false;
        throw new Error('canvas nepregătit');
      }
      return { frame: FRAME };
    });

    await loop.tick();
    await loop.tick();

    expect(onCode).toHaveBeenCalledExactlyOnceWith('COD-1');
  });

  it('nu pornește o citire nouă cât timp cea veche încă rulează', async () => {
    let release!: (text: string) => void;
    const slow = { read: vi.fn(() => new Promise<string | null>((resolve) => (release = resolve))) };
    const { loop, onCode } = create(slow);

    const first = loop.tick();
    await loop.tick(); // cât prima e în curs: ignorat
    await loop.tick();
    expect(slow.read).toHaveBeenCalledTimes(1);

    release('COD-1');
    await first;
    expect(onCode).toHaveBeenCalledTimes(1);
  });

  it('transmite și sursa pentru detectorul nativ către cititor', async () => {
    const source = {} as CanvasImageSource;
    const reader = scriptedReader('COD-1');
    const { loop } = create(reader, vi.fn(), () => ({ frame: FRAME, source }));

    await loop.tick();

    expect(reader.read).toHaveBeenCalledWith(FRAME, source);
  });

  describe('pornire și oprire', () => {
    it('citește periodic după start()', async () => {
      const reader = scriptedReader('COD-1');
      const { loop, onCode } = create(reader);

      loop.start();
      await vi.advanceTimersByTimeAsync(OPTIONS.intervalMs * 3);

      expect(reader.read.mock.calls.length).toBeGreaterThanOrEqual(3);
      expect(onCode).toHaveBeenCalledTimes(1);
      loop.stop();
    });

    it('nu mai citește după stop()', async () => {
      const reader = scriptedReader(...Array(50).fill('COD-1'));
      const { loop } = create(reader);
      loop.start();
      await vi.advanceTimersByTimeAsync(OPTIONS.intervalMs * 2);

      loop.stop();
      const callsAtStop = reader.read.mock.calls.length;
      await vi.advanceTimersByTimeAsync(OPTIONS.intervalMs * 10);

      expect(reader.read.mock.calls.length).toBe(callsAtStop);
    });

    it('un start() repetat nu creează o a doua buclă', async () => {
      const reader = scriptedReader(...Array(50).fill(null));
      const { loop } = create(reader);

      loop.start();
      loop.start();
      loop.start();
      await vi.advanceTimersByTimeAsync(OPTIONS.intervalMs * 5);

      expect(reader.read.mock.calls.length).toBeLessThanOrEqual(5);
      loop.stop();
    });

    it('după stop(), un cod citit „în zbor” nu mai este anunțat', async () => {
      let release!: (text: string) => void;
      const slow = { read: vi.fn(() => new Promise<string | null>((resolve) => (release = resolve))) };
      const { loop, onCode } = create(slow);

      const pending = loop.tick();
      loop.stop();
      release('COD-1');
      await pending;

      expect(onCode).not.toHaveBeenCalled();
    });

    it('după stop(), start() nu mai repornește bucla', async () => {
      const reader = scriptedReader(...Array(50).fill('COD-1'));
      const { loop } = create(reader);

      loop.stop();
      loop.start();
      await vi.advanceTimersByTimeAsync(OPTIONS.intervalMs * 5);

      expect(reader.read).not.toHaveBeenCalled();
    });
  });
});
