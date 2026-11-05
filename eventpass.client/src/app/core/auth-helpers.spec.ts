import { HttpErrorResponse } from '@angular/common/http';
import { safeReturnUrl } from '../pages/auth/login';
import { errorMessage } from './http-errors';

describe('safeReturnUrl', () => {
  it('acceptă adrese din aplicație', () => {
    expect(safeReturnUrl('/evenimente/7')).toBe('/evenimente/7');
  });

  it('respinge alte site-uri (redirecționare deschisă)', () => {
    expect(safeReturnUrl('https://site-rau.com')).toBe('/');
    expect(safeReturnUrl('//site-rau.com')).toBe('/');
  });

  it('fără adresă, duce la pagina principală', () => {
    expect(safeReturnUrl(undefined)).toBe('/');
  });
});

describe('errorMessage', () => {
  it('folosește mesajul trimis de server', () => {
    const err = new HttpErrorResponse({ status: 409, error: { message: 'Biletele s-au epuizat.' } });
    expect(errorMessage(err)).toBe('Biletele s-au epuizat.');
  });

  it('ia prima eroare de validare', () => {
    const err = new HttpErrorResponse({ status: 400, error: { errors: { Password: ['Parola e prea scurtă.'] } } });
    expect(errorMessage(err)).toBe('Parola e prea scurtă.');
  });

  it('spune clar când serverul nu răspunde', () => {
    const err = new HttpErrorResponse({ status: 0 });
    expect(errorMessage(err)).toContain('Serverul nu răspunde');
  });
});
