import { inject, Injectable, NgZone } from '@angular/core';
import { CameraError, CameraService, toCameraError } from './camera.service';
import { QrReader } from './qr-reader';
import { GrabbedFrame, ScanLoop } from './scan-loop';

export interface ScannerSession {
  stop(): void;
}

// Imaginea se micșorează la cel mult atâția pixeli pe latura mare: destul cât să se vadă clar un cod QR,
// dar fără să încetinească telefonul cu cadre uriașe
const MAX_SIDE = 720;
const INTERVAL_MS = 120;
// Un cod rămas în fața camerei se raportează o singură dată; din nou abia după ce lipsește atâta timp
const ABSENCE_MS = 1500;

// Leagă camera, imaginea și bucla de scanare. Pagina cere doar „pornește” și primește codurile citite.
@Injectable({ providedIn: 'root' })
export class ScannerEngine {
  private readonly camera = inject(CameraService);
  private readonly reader = inject(QrReader);
  private readonly zone = inject(NgZone);

  // Pornește camera în elementul <video> și citește coduri până la stop().
  // `onEnded` se apelează dacă camera se oprește singură (ex. ecranul s-a blocat, cablul scos).
  async start(video: HTMLVideoElement, onCode: (text: string) => void, onEnded?: () => void): Promise<ScannerSession> {
    const stream = await this.camera.open();
    const release = () => {
      stream.getTracks().forEach((track) => track.stop());
      video.srcObject = null;
    };

    try {
      video.srcObject = stream;
      video.muted = true;
      video.setAttribute('playsinline', 'true'); // fără asta, iPhone-ul deschide videoul pe tot ecranul
      await video.play();
    } catch (error) {
      release();
      throw toCameraError(error);
    }

    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d', { willReadFrequently: true });
    if (!context) {
      release();
      throw new CameraError('unknown', 'Nu am putut pregăti imaginea pentru citire. Încearcă din nou.');
    }

    const grab = (): GrabbedFrame | null => {
      // Camera încă nu a dat niciun cadru
      if (video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA || !video.videoWidth || !video.videoHeight) return null;
      const scale = Math.min(1, MAX_SIDE / Math.max(video.videoWidth, video.videoHeight));
      const width = Math.round(video.videoWidth * scale);
      const height = Math.round(video.videoHeight * scale);
      if (canvas.width !== width) canvas.width = width;
      if (canvas.height !== height) canvas.height = height;
      context.drawImage(video, 0, 0, width, height);
      return { frame: context.getImageData(0, 0, width, height), source: canvas };
    };

    // Rezultatul se întoarce în „zona” Angular doar când e un cod citit; altfel pagina s-ar reîmprospăta
    // la fiecare cadru (de ~8 ori pe secundă), degeaba
    const loop = new ScanLoop(grab, this.reader, (text) => this.zone.run(() => onCode(text)), {
      intervalMs: INTERVAL_MS,
      absenceMs: ABSENCE_MS,
    });
    this.zone.runOutsideAngular(() => loop.start());

    let stopped = false;
    const stop = () => {
      if (stopped) return;
      stopped = true;
      loop.stop();
      release();
    };

    stream.getTracks().forEach((track) =>
      track.addEventListener('ended', () => {
        if (stopped) return;
        stop();
        this.zone.run(() => onEnded?.());
      })
    );

    return { stop };
  }
}
