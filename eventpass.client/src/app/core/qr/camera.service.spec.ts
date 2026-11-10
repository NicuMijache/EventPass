import { TestBed } from '@angular/core/testing';
import { CameraError, CameraService, toCameraError } from './camera.service';

const domError = (name: string) => Object.assign(new Error(name), { name });

describe('toCameraError', () => {
  it.each([
    ['NotAllowedError', 'denied'],
    ['PermissionDeniedError', 'denied'],
    ['SecurityError', 'denied'],
    ['NotFoundError', 'not-found'],
    ['DevicesNotFoundError', 'not-found'],
    ['NotReadableError', 'busy'],
    ['TrackStartError', 'busy'],
    ['AbortError', 'busy'],
    ['OverconstrainedError', 'unknown'],
    ['ceva-necunoscut', 'unknown'],
  ])('%s → %s', (name, kind) => {
    const error = toCameraError(domError(name));

    expect(error).toBeInstanceOf(CameraError);
    expect(error.kind).toBe(kind);
    expect(error.message.length).toBeGreaterThan(20);
  });

  it('mesajul pentru permisiune refuzată îi spune utilizatorului ce să facă', () => {
    expect(toCameraError(domError('NotAllowedError')).message).toContain('Permite camera');
  });

  it('nu încapsulează de două ori o eroare deja procesată', () => {
    const once = toCameraError(domError('NotFoundError'));

    expect(toCameraError(once)).toBe(once);
  });

  it('suportă valori ciudate (null, text, număr) fără să arunce', () => {
    expect(toCameraError(null).kind).toBe('unknown');
    expect(toCameraError('eroare').kind).toBe('unknown');
    expect(toCameraError(42).kind).toBe('unknown');
  });
});

describe('CameraService', () => {
  const originalDevices = Object.getOwnPropertyDescriptor(navigator, 'mediaDevices');
  const originalSecure = Object.getOwnPropertyDescriptor(globalThis, 'isSecureContext');

  function setDevices(value: unknown) {
    Object.defineProperty(navigator, 'mediaDevices', { value, configurable: true });
  }

  function setSecure(value: boolean) {
    Object.defineProperty(globalThis, 'isSecureContext', { value, configurable: true });
  }

  afterEach(() => {
    if (originalDevices) Object.defineProperty(navigator, 'mediaDevices', originalDevices);
    else delete (navigator as { mediaDevices?: unknown }).mediaDevices;
    if (originalSecure) Object.defineProperty(globalThis, 'isSecureContext', originalSecure);
    else delete (globalThis as { isSecureContext?: unknown }).isSecureContext;
  });

  const open = () => TestBed.inject(CameraService).open();

  it('cere camera din spate (preferat) și fără sunet, apoi întoarce fluxul', async () => {
    const stream = { id: 'flux' } as unknown as MediaStream;
    const getUserMedia = vi.fn().mockResolvedValue(stream);
    setSecure(true);
    setDevices({ getUserMedia });

    const result = await open();

    expect(result).toBe(stream);
    const constraints = getUserMedia.mock.calls[0][0];
    expect(constraints.audio).toBe(false);
    expect(constraints.video.facingMode).toEqual({ ideal: 'environment' });
  });

  it('pe o pagină nesigură (HTTP, nu localhost) refuză cu un mesaj despre HTTPS, fără să ceară camera', async () => {
    const getUserMedia = vi.fn();
    setSecure(false);
    setDevices({ getUserMedia });

    await expect(open()).rejects.toMatchObject({ kind: 'insecure', message: expect.stringContaining('HTTPS') });
    expect(getUserMedia).not.toHaveBeenCalled();
  });

  it('dacă browserul nu are API-ul pentru cameră, spune că nu e suportat', async () => {
    setSecure(true);
    setDevices(undefined);

    await expect(open()).rejects.toMatchObject({ kind: 'unsupported' });
  });

  it('permisiune refuzată → eroare „denied”', async () => {
    setSecure(true);
    setDevices({ getUserMedia: vi.fn().mockRejectedValue(domError('NotAllowedError')) });

    await expect(open()).rejects.toMatchObject({ kind: 'denied' });
  });

  it('fără cameră → eroare „not-found”', async () => {
    setSecure(true);
    setDevices({ getUserMedia: vi.fn().mockRejectedValue(domError('NotFoundError')) });

    await expect(open()).rejects.toMatchObject({ kind: 'not-found' });
  });

  it('când restricțiile nu sunt acceptate (OverconstrainedError), reîncearcă simplu și reușește', async () => {
    const stream = { id: 'flux' } as unknown as MediaStream;
    const getUserMedia = vi.fn().mockRejectedValueOnce(domError('OverconstrainedError')).mockResolvedValueOnce(stream);
    setSecure(true);
    setDevices({ getUserMedia });

    const result = await open();

    expect(result).toBe(stream);
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(getUserMedia.mock.calls[1][0]).toEqual({ video: true, audio: false });
  });

  it('dacă și reîncercarea eșuează, întoarce eroarea reîncercării (cu mesaj pe înțelesul utilizatorului)', async () => {
    const getUserMedia = vi.fn().mockRejectedValueOnce(domError('OverconstrainedError')).mockRejectedValueOnce(domError('NotAllowedError'));
    setSecure(true);
    setDevices({ getUserMedia });

    await expect(open()).rejects.toMatchObject({ kind: 'denied' });
  });

  it('un localhost (secure context true) merge normal', async () => {
    setSecure(true);
    setDevices({ getUserMedia: vi.fn().mockResolvedValue({}) });

    await expect(open()).resolves.toBeDefined();
  });
});
