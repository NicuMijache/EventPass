import { Injectable } from '@angular/core';

export type CameraErrorKind =
  | 'insecure' // pagina nu e HTTPS (și nu e localhost)
  | 'unsupported' // browserul nu știe să acceseze camera
  | 'denied' // utilizatorul (sau browserul) a refuzat permisiunea
  | 'not-found' // dispozitivul nu are cameră
  | 'busy' // camera e folosită de altă aplicație sau nu pornește
  | 'unknown';

// O eroare de cameră cu un mesaj gata de afișat utilizatorului
export class CameraError extends Error {
  constructor(
    readonly kind: CameraErrorKind,
    message: string
  ) {
    super(message);
    this.name = 'CameraError';
  }
}

const MESSAGES: Record<CameraErrorKind, string> = {
  insecure:
    'Camera funcționează doar pe pagini securizate (HTTPS) sau pe localhost. Deschide site-ul prin HTTPS ca să poți scana.',
  unsupported: 'Acest browser nu permite accesul la cameră. Încearcă Chrome sau Safari actualizat.',
  denied: 'Accesul la cameră a fost refuzat. Permite camera din setările browserului pentru acest site, apoi încearcă din nou.',
  'not-found': 'Nu am găsit nicio cameră pe acest dispozitiv. Poți introduce codul de mână.',
  busy: 'Camera nu poate fi pornită. Poate e folosită de altă aplicație: închide-o și încearcă din nou.',
  unknown: 'Nu am putut porni camera. Încearcă din nou sau introdu codul de mână.',
};

// Transformă eroarea brută a browserului (DOMException) într-una cu mesaj pe înțelesul utilizatorului
export function toCameraError(error: unknown): CameraError {
  if (error instanceof CameraError) return error;

  const name = (error as { name?: string } | null)?.name ?? '';
  let kind: CameraErrorKind;
  switch (name) {
    case 'NotAllowedError':
    case 'PermissionDeniedError':
    case 'SecurityError':
      kind = 'denied';
      break;
    case 'NotFoundError':
    case 'DevicesNotFoundError':
      kind = 'not-found';
      break;
    case 'NotReadableError':
    case 'TrackStartError':
    case 'AbortError':
      kind = 'busy';
      break;
    default:
      kind = 'unknown';
  }
  return new CameraError(kind, MESSAGES[kind]);
}

// Deschide camera telefonului. Ordinea preferințelor: camera din spate (pentru scanat), apoi orice cameră.
@Injectable({ providedIn: 'root' })
export class CameraService {
  async open(): Promise<MediaStream> {
    // Browserele permit camera doar pe pagini „sigure”: HTTPS sau localhost. Verificăm explicit,
    // ca utilizatorul să primească un mesaj clar în loc de o eroare tehnică.
    if (globalThis.isSecureContext === false) {
      throw new CameraError('insecure', MESSAGES.insecure);
    }
    const devices = globalThis.navigator?.mediaDevices;
    if (!devices?.getUserMedia) {
      throw new CameraError('unsupported', MESSAGES.unsupported);
    }

    try {
      return await devices.getUserMedia({
        // „ideal”, nu „exact”: dacă nu există cameră în spate (ex. laptop), se folosește cea disponibilă
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
    } catch (error) {
      // Unele camere nu acceptă restricțiile cerute: încercăm din nou, fără ele
      if ((error as { name?: string })?.name === 'OverconstrainedError') {
        try {
          return await devices.getUserMedia({ video: true, audio: false });
        } catch (retryError) {
          throw toCameraError(retryError);
        }
      }
      throw toCameraError(error);
    }
  }
}
