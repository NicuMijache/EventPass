// Ce înseamnă textul citit dintr-un cod: un bilet EventPass sau altceva?
// Regulile oglindesc backend-ul (TicketCode.cs): tokenul unui bilet e un GUID; în QR e scris cu majuscule.

export type ParsedTicketCode =
  // Tokenul întreg, normalizat (majuscule, cu cratime). Vine din QR sau e tastat de mână.
  | { kind: 'token'; token: string }
  // Codul scurt de pe bilet (primele 8 caractere). Se poate tasta de mână; serverul îl rezolvă (#17).
  | { kind: 'short'; code: string };

const GUID_WITH_HYPHENS = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const GUID_WITHOUT_HYPHENS = /^[0-9a-f]{32}$/i;
const SHORT_CODE = /^[0-9a-f]{8}$/i;

// Textul unui cod scanat cu camera: acceptăm DOAR un token întreg. Orice altceva (un link, un cod
// de pe un produs, un cod scurt) nu e un bilet EventPass.
export function parseScannedCode(raw: string): { kind: 'token'; token: string } | null {
  const parsed = parseTicketCode(raw);
  return parsed?.kind === 'token' ? parsed : null;
}

// Textul tastat de mână: token întreg sau cod scurt, cu sau fără spații (ex. „FA46 C34B”).
export function parseTicketCode(raw: string): ParsedTicketCode | null {
  const text = raw.replace(/\s+/g, '');

  if (GUID_WITH_HYPHENS.test(text)) {
    return { kind: 'token', token: text.toUpperCase() };
  }
  if (GUID_WITHOUT_HYPHENS.test(text)) {
    const t = text.toUpperCase();
    return { kind: 'token', token: `${t.slice(0, 8)}-${t.slice(8, 12)}-${t.slice(12, 16)}-${t.slice(16, 20)}-${t.slice(20)}` };
  }
  if (SHORT_CODE.test(text)) {
    return { kind: 'short', code: text.toUpperCase() };
  }
  return null;
}

// Codul scurt al unui token, ca pe bilet (primele 8 caractere)
export function shortCodeOf(token: string): string {
  return token.replace(/-/g, '').slice(0, 8).toUpperCase();
}
