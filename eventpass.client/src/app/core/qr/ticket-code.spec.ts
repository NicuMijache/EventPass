import { parseScannedCode, parseTicketCode, shortCodeOf } from './ticket-code';

const TOKEN = '3F2A9C1B-7D4E-4A2B-9C81-5E6F7A8B9C0D';

describe('parseTicketCode (tastat de mână)', () => {
  it('acceptă tokenul întreg cu majuscule', () => {
    expect(parseTicketCode(TOKEN)).toEqual({ kind: 'token', token: TOKEN });
  });

  it('acceptă tokenul cu litere mici și îl face cu majuscule', () => {
    expect(parseTicketCode(TOKEN.toLowerCase())).toEqual({ kind: 'token', token: TOKEN });
  });

  it('acceptă tokenul fără cratime și îl rescrie cu cratime', () => {
    expect(parseTicketCode(TOKEN.replace(/-/g, ''))).toEqual({ kind: 'token', token: TOKEN });
  });

  it('ignoră spațiile de la margini și din interior', () => {
    expect(parseTicketCode(`  ${TOKEN}  `)).toEqual({ kind: 'token', token: TOKEN });
    expect(parseTicketCode('fa46 c34b')).toEqual({ kind: 'short', code: 'FA46C34B' });
  });

  it('acceptă codul scurt de 8 caractere', () => {
    expect(parseTicketCode('FA46C34B')).toEqual({ kind: 'short', code: 'FA46C34B' });
    expect(parseTicketCode('fa46c34b')).toEqual({ kind: 'short', code: 'FA46C34B' });
  });

  it.each([
    ['', 'gol'],
    ['   ', 'doar spații'],
    ['FA46C34', '7 caractere'],
    ['FA46C34B1', '9 caractere'],
    ['FA46C34G', 'literă în afara lui A–F'],
    ['3F2A9C1B-7D4E-4A2B-9C81-5E6F7A8B9C0', 'token cu un caracter lipsă'],
    ['3F2A9C1B-7D4E-4A2B-9C81-5E6F7A8B9C0DD', 'token cu un caracter în plus'],
    ['3F2A9C1B_7D4E_4A2B_9C81_5E6F7A8B9C0D', 'alt separator'],
    ['https://eventpass.example/bilet', 'un link'],
    ['<script>alert(1)</script>', 'cod HTML'],
    ['{3F2A9C1B-7D4E-4A2B-9C81-5E6F7A8B9C0D}', 'GUID cu acolade'],
  ])('respinge %j (%s)', (text) => {
    expect(parseTicketCode(text)).toBeNull();
  });
});

describe('parseScannedCode (citit cu camera)', () => {
  it('acceptă un token', () => {
    expect(parseScannedCode(TOKEN)).toEqual({ kind: 'token', token: TOKEN });
  });

  it('NU acceptă un cod scurt: un QR care conține doar 8 caractere nu e al nostru', () => {
    expect(parseScannedCode('FA46C34B')).toBeNull();
  });

  it('nu acceptă un link sau alt text', () => {
    expect(parseScannedCode('https://example.com')).toBeNull();
    expect(parseScannedCode('Meniu restaurant')).toBeNull();
  });
});

describe('shortCodeOf', () => {
  it('întoarce primele 8 caractere ale tokenului, ca pe bilet', () => {
    expect(shortCodeOf(TOKEN)).toBe('3F2A9C1B');
  });

  it('merge și cu litere mici sau fără cratime', () => {
    expect(shortCodeOf(TOKEN.toLowerCase())).toBe('3F2A9C1B');
    expect(shortCodeOf(TOKEN.replace(/-/g, ''))).toBe('3F2A9C1B');
  });
});
