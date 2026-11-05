import { HttpErrorResponse } from '@angular/common/http';

// Transformă un răspuns de eroare de la API într-un mesaj pentru utilizator.
// Serverul trimite fie { message: "..." }, fie erori de validare { errors: { Camp: ["..."] } }.
export function errorMessage(err: unknown, fallback = 'A apărut o eroare. Încearcă din nou.'): string {
  if (!(err instanceof HttpErrorResponse)) return fallback;
  if (err.status === 0) return 'Serverul nu răspunde. Verifică dacă API-ul e pornit.';

  const body = err.error;
  if (body && typeof body === 'object') {
    if (typeof body.message === 'string') return body.message;
    if (body.errors && typeof body.errors === 'object') {
      const first = Object.values(body.errors as Record<string, string[]>).flat()[0];
      if (first) return first;
    }
  }
  return fallback;
}
