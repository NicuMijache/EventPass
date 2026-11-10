import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { SCANNER_ROLES, User, UserRole } from '../models/auth.models';
import { AuthService } from '../services/auth.service';
import { roleGuard } from './auth.guard';

describe('roleGuard', () => {
  function setup(session: { token: string | null; user: User | null }) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: { token: () => session.token, user: () => session.user } }],
    });
    const router = TestBed.inject(Router);
    const run = () =>
      TestBed.runInInjectionContext(() =>
        roleGuard(SCANNER_ROLES)({} as ActivatedRouteSnapshot, { url: '/scaneaza' } as RouterStateSnapshot)
      );
    return { router, run };
  }

  const user = (role: UserRole): User => ({ id: 1, email: 'a@b.ro', fullName: 'Test', role });

  it.each<UserRole>(['Staff', 'Organizer', 'Admin'])('lasă să treacă rolul %s', (role) => {
    const { run } = setup({ token: 'token', user: user(role) });

    expect(run()).toBe(true);
  });

  it('un Client este trimis la pagina principală', () => {
    const { run, router } = setup({ token: 'token', user: user('Client') });

    const result = run() as UrlTree;

    expect(router.serializeUrl(result)).toBe('/');
  });

  it('fără login, trimite la login și ține minte pagina dorită', () => {
    const { run, router } = setup({ token: null, user: null });

    const result = run() as UrlTree;

    expect(router.serializeUrl(result)).toBe('/login?returnUrl=%2Fscaneaza');
  });

  it('cu token, dar fără utilizator încărcat, nu lasă să treacă', () => {
    const { run, router } = setup({ token: 'token', user: null });

    expect(router.serializeUrl(run() as UrlTree)).toBe('/');
  });

  it('Clientul nu e în lista celor care scanează', () => {
    expect(SCANNER_ROLES).not.toContain('Client');
    expect([...SCANNER_ROLES].sort()).toEqual(['Admin', 'Organizer', 'Staff']);
  });
});
