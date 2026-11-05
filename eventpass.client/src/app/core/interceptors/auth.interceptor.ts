import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

// Prin acest „filtru” trece orice cerere HTTP făcută de aplicație.
// 1) Adaugă automat tokenul la cererile către /api.
// 2) Dacă serverul răspunde 401 (token expirat/invalid), închide sesiunea și trimite la login.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  const token = req.url.startsWith('/api') ? auth.token() : null;
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((err: HttpErrorResponse) => {
      // La login un 401 înseamnă doar „parolă greșită”, nu sesiune expirată
      if (err.status === 401 && token && !req.url.startsWith('/api/auth/')) {
        auth.clear();
        router.navigate(['/login'], { queryParams: { returnUrl: router.url, expired: 1 } });
      }
      return throwError(() => err);
    })
  );
};
