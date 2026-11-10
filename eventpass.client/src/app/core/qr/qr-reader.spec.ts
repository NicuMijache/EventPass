import { TestBed } from '@angular/core/testing';
import { FrameData, QrReader } from './qr-reader';
import { QR_FIXTURES } from './testing/qr-fixtures';

// Desenează matricea de module ca pixeli RGBA, exact ca PNG-ul generat de API (pătrățele negre pe alb)
function render(matrix: string[], pixelsPerModule: number): FrameData {
  const modules = matrix.length;
  const width = modules * pixelsPerModule;
  const data = new Uint8ClampedArray(width * width * 4);
  for (let y = 0; y < width; y++) {
    for (let x = 0; x < width; x++) {
      const dark = matrix[Math.floor(y / pixelsPerModule)][Math.floor(x / pixelsPerModule)] === '1';
      const i = (y * width + x) * 4;
      data[i] = data[i + 1] = data[i + 2] = dark ? 0 : 255;
      data[i + 3] = 255;
    }
  }
  return { data, width, height: width };
}

function blank(width: number, height: number): FrameData {
  return { data: new Uint8ClampedArray(width * height * 4).fill(255), width, height };
}

describe('QrReader', () => {
  // Fără detector nativ (cum e în testele astea, în jsdom) se folosește jsQR
  const read = (frame: FrameData) => TestBed.inject(QrReader).read(frame);

  it('are fixturi cu ambele feluri de coduri', () => {
    expect(QR_FIXTURES.filter((f) => !f.zxingFails).length).toBeGreaterThanOrEqual(3);
    expect(QR_FIXTURES.filter((f) => f.zxingFails).length).toBeGreaterThanOrEqual(5);
  });

  it.each(QR_FIXTURES.map((f) => [f.token, f]))('citește codul biletului %s', async (_token, fixture) => {
    expect(await read(render(fixture.matrix, 10))).toBe(fixture.token);
  });

  it('citește TOATE codurile, inclusiv cele pe care ZXing nu le citește (motivul pentru care am ales jsQR)', async () => {
    const hard = QR_FIXTURES.filter((f) => f.zxingFails);
    for (const fixture of hard) {
      expect(await read(render(fixture.matrix, 10))).toBe(fixture.token);
    }
  });

  it.each([3, 4, 6, 8, 14])('citește codul și când pătrățelele au %i pixeli (distanță diferită de cameră)', async (scale) => {
    const fixture = QR_FIXTURES[0];
    expect(await read(render(fixture.matrix, scale))).toBe(fixture.token);
  });

  it('citește codul pus într-un cadru mai mare, cu fundal alb în jur (ca în fața unei camere)', async () => {
    const fixture = QR_FIXTURES[0];
    const qr = render(fixture.matrix, 6);
    const width = 640;
    const height = 480;
    const frame = blank(width, height);
    const offsetX = 200;
    const offsetY = 120;
    for (let y = 0; y < qr.height; y++) {
      for (let x = 0; x < qr.width; x++) {
        const from = (y * qr.width + x) * 4;
        const to = ((y + offsetY) * width + (x + offsetX)) * 4;
        frame.data.set(qr.data.subarray(from, from + 4), to);
      }
    }
    expect(await read(frame)).toBe(fixture.token);
  });

  it('întoarce null când în imagine nu e niciun cod', async () => {
    expect(await read(blank(320, 240))).toBeNull();
  });

  it('întoarce null pentru zgomot aleatoriu', async () => {
    const width = 200;
    const data = new Uint8ClampedArray(width * width * 4);
    let seed = 7;
    for (let i = 0; i < data.length; i += 4) {
      seed = (seed * 1103515245 + 12345) & 0x7fffffff;
      const v = seed % 256;
      data[i] = data[i + 1] = data[i + 2] = v;
      data[i + 3] = 255;
    }
    expect(await read({ data, width, height: width })).toBeNull();
  });

  it('nu citește un cod acoperit în mare parte', async () => {
    const fixture = QR_FIXTURES[0];
    const frame = render(fixture.matrix, 10);
    for (let y = frame.height * 0.2; y < frame.height * 0.8; y++) {
      for (let x = frame.width * 0.2; x < frame.width * 0.8; x++) {
        const i = (Math.floor(y) * frame.width + Math.floor(x)) * 4;
        frame.data[i] = frame.data[i + 1] = frame.data[i + 2] = 255;
      }
    }
    expect(await read(frame)).toBeNull();
  });

  describe('cu detectorul nativ al browserului', () => {
    afterEach(() => {
      delete (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector;
    });

    function withNative(detect: () => Promise<{ rawValue: string }[]>) {
      class FakeDetector {
        static created = 0;
        constructor() {
          FakeDetector.created++;
        }
        detect = detect;
      }
      (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector = FakeDetector;
      return FakeDetector;
    }

    const source = {} as CanvasImageSource;

    it('folosește rezultatul detectorului nativ când găsește ceva', async () => {
      withNative(async () => [{ rawValue: 'DIN-NATIV' }]);

      // Imagine goală: jsQR n-ar fi găsit nimic, deci rezultatul vine sigur de la detectorul nativ
      const text = await TestBed.inject(QrReader).read(blank(100, 100), source);

      expect(text).toBe('DIN-NATIV');
    });

    it('trece la jsQR când detectorul nativ nu găsește nimic', async () => {
      withNative(async () => []);
      const fixture = QR_FIXTURES[0];

      const text = await TestBed.inject(QrReader).read(render(fixture.matrix, 10), source);

      expect(text).toBe(fixture.token);
    });

    it('trece la jsQR când detectorul nativ aruncă o eroare', async () => {
      withNative(async () => {
        throw new Error('detector stricat');
      });
      const fixture = QR_FIXTURES[1];

      const text = await TestBed.inject(QrReader).read(render(fixture.matrix, 10), source);

      expect(text).toBe(fixture.token);
    });

    it('ignoră un rezultat nativ gol și încearcă jsQR', async () => {
      withNative(async () => [{ rawValue: '' }]);
      const fixture = QR_FIXTURES[2];

      expect(await TestBed.inject(QrReader).read(render(fixture.matrix, 10), source)).toBe(fixture.token);
    });

    it('fără o sursă pentru detectorul nativ, merge direct pe jsQR', async () => {
      const Detector = withNative(async () => [{ rawValue: 'NU-AR-TREBUI' }]);
      const fixture = QR_FIXTURES[0];

      const text = await TestBed.inject(QrReader).read(render(fixture.matrix, 10));

      expect(text).toBe(fixture.token);
      expect(Detector.created).toBe(1);
    });

    it('dacă platforma are clasa dar nu știe QR (constructorul aruncă), merge pe jsQR', async () => {
      (globalThis as { BarcodeDetector?: unknown }).BarcodeDetector = class {
        constructor() {
          throw new Error('qr_code nu e suportat');
        }
      };
      const fixture = QR_FIXTURES[0];

      expect(await TestBed.inject(QrReader).read(render(fixture.matrix, 10), source)).toBe(fixture.token);
    });
  });
});
