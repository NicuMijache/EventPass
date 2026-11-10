import { Injectable } from '@angular/core';
import jsQR from 'jsqr';

// O imagine ca șir de pixeli RGBA (formatul lui ImageData, dar fără să depindă de el, ca să se poată testa)
export interface FrameData {
  data: Uint8ClampedArray;
  width: number;
  height: number;
}

// Detectorul de coduri al browserului (BarcodeDetector). Nu există peste tot (ex. nu pe iPhone sau pe Chrome
// pentru Windows), de aceea e doar o primă încercare, nu o cerință.
interface NativeDetector {
  detect(source: CanvasImageSource): Promise<{ rawValue: string }[]>;
}

type NativeDetectorConstructor = new (options: { formats: string[] }) => NativeDetector;

function createNativeDetector(): NativeDetector | null {
  const ctor = (globalThis as { BarcodeDetector?: NativeDetectorConstructor }).BarcodeDetector;
  if (!ctor) return null;
  try {
    return new ctor({ formats: ['qr_code'] });
  } catch {
    // Unele platforme au clasa, dar nu știu să citească QR
    return null;
  }
}

// Citește un cod QR dintr-o imagine. Ordinea:
//  1. detectorul nativ al browserului (rapid, dacă există);
//  2. jsQR (merge peste tot). Am ales jsQR pentru că a citit 200 din 200 de coduri generate de noi,
//     în timp ce biblioteca ZXing (din html5-qrcode) pierde ~1-10% dintre ele.
@Injectable({ providedIn: 'root' })
export class QrReader {
  private readonly native = createNativeDetector();

  // `nativeSource` = aceeași imagine, într-o formă pe care o înțelege detectorul nativ (de ex. un canvas)
  async read(frame: FrameData, nativeSource?: CanvasImageSource): Promise<string | null> {
    if (this.native && nativeSource) {
      try {
        const found = (await this.native.detect(nativeSource)).find((code) => code.rawValue);
        if (found) return found.rawValue;
      } catch {
        // Detectorul nativ a dat greș pe acest cadru; încercăm jsQR
      }
    }
    // „dontInvert”: codurile noastre sunt mereu negru pe alb; să nu căutăm și varianta inversată
    // (ar dubla timpul de citire pentru fiecare cadru)
    const result = jsQR(frame.data, frame.width, frame.height, { inversionAttempts: 'dontInvert' });
    return result?.data ?? null;
  }
}
