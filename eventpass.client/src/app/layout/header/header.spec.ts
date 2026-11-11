import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { User, UserRole } from '../../core/models/auth.models';
import { AuthService } from '../../core/services/auth.service';
import { Header } from './header';

describe('Header: linkul „Scanează”', () => {
  const user = signal<User | null>(null);
  let fixture: ComponentFixture<Header>;

  beforeEach(() => {
    user.set(null);
    TestBed.configureTestingModule({
      imports: [Header],
      providers: [provideRouter([]), { provide: AuthService, useValue: { user, logout: () => undefined } }],
    });
    fixture = TestBed.createComponent(Header);
    fixture.detectChanges();
  });

  const scanLink = () => (fixture.nativeElement as HTMLElement).querySelector('a[href="/scaneaza"]');

  function loginAs(role: UserRole) {
    user.set({ id: 1, email: 'a@b.ro', fullName: 'Test User', role });
    fixture.detectChanges();
  }

  it('fără login, linkul nu apare', () => {
    expect(scanLink()).toBeNull();
  });

  it('un Client nu vede linkul', () => {
    loginAs('Client');

    expect(scanLink()).toBeNull();
  });

  it.each<UserRole>(['Staff', 'Organizer', 'Admin'])('%s vede linkul', (role) => {
    loginAs(role);

    expect(scanLink()?.textContent?.trim()).toBe('Scanează');
  });

  it('linkul dispare când utilizatorul iese din cont', () => {
    loginAs('Staff');
    expect(scanLink()).not.toBeNull();

    user.set(null);
    fixture.detectChanges();

    expect(scanLink()).toBeNull();
  });

  it('linkul „Evenimente” rămâne mereu', () => {
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/"].nav__link')).not.toBeNull();
    loginAs('Client');
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/"].nav__link')).not.toBeNull();
  });

  // Panoul organizatorului (#20)
  const panelLink = () => (fixture.nativeElement as HTMLElement).querySelector('a[href="/organizator"]');

  it('„Panou” apare doar pentru organizatori', () => {
    expect(panelLink()).toBeNull();
    for (const role of ['Client', 'Staff', 'Admin'] as UserRole[]) {
      loginAs(role);
      expect(panelLink()).toBeNull();
    }

    loginAs('Organizer');
    expect(panelLink()?.textContent?.trim()).toBe('Panou');
  });
});
