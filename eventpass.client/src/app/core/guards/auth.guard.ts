import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { UserRole } from '../models/auth.models';
import { AuthService } from '../services/auth.service';

// „Paznicul” rutelor care cer cont: fără login, te trimite la /login
// și ține minte unde voiai să ajungi (returnUrl), ca să te întoarcă acolo după.
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.token()) {
    return true;
  }
  return inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

// Pagini doar pentru anumite roluri (ex. scannerul). Fără login: la login, cu întoarcere aici după.
// Logat, dar cu alt rol: înapoi la pagina principală.
// ATENȚIE: asta doar ascunde paginile. Rolul din browser poate fi vechi sau modificat de utilizator,
// deci SERVERUL e cel care decide la fiecare acțiune (vezi validarea biletelor).
export function roleGuard(allowed: readonly UserRole[]): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    const router = inject(Router);
    if (!auth.token()) {
      return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
    }
    const role = auth.user()?.role;
    return role && allowed.includes(role) ? true : router.createUrlTree(['/']);
  };
}

// Invers: pagina de login nu are sens dacă ești deja logat
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.token() ? inject(Router).createUrlTree(['/']) : true;
};
