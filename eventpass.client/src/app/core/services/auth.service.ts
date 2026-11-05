import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { AuthResponse, LoginRequest, RegisterRequest, User } from '../models/auth.models';

// Cheia sub care păstrăm sesiunea în localStorage (memoria browserului)
const STORAGE_KEY = 'eventpass.auth';

interface StoredSession {
  token: string;
  expiresAt: string;
  user: User;
}

// Ține minte cine e logat. Restul aplicației citește `user()` / `isLoggedIn()`
// și se actualizează singur când utilizatorul intră sau iese (semnale).
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<StoredSession | null>(this.readStoredSession());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);

  // Tokenul e citit de interceptor; dacă a expirat, ieșim automat
  token(): string | null {
    const s = this.session();
    if (!s) return null;
    if (new Date(s.expiresAt).getTime() <= Date.now()) {
      this.clear();
      return null;
    }
    return s.token;
  }

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/login', request).pipe(tap((res) => this.store(res)));
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http.post<AuthResponse>('/api/auth/register', request).pipe(tap((res) => this.store(res)));
  }

  logout(): void {
    this.clear();
    this.router.navigateByUrl('/');
  }

  // Folosit de interceptor când serverul răspunde 401 (token expirat sau invalid)
  clear(): void {
    this.session.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      // localStorage poate fi blocat (ex. mod privat); sesiunea rămâne doar în memorie
    }
  }

  private store(res: AuthResponse): void {
    const session: StoredSession = { token: res.token, expiresAt: res.expiresAt, user: res.user };
    this.session.set(session);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch {
      // vezi mai sus
    }
  }

  private readStoredSession(): StoredSession | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as StoredSession;
      // O sesiune expirată nu are ce căuta înapoi
      if (new Date(session.expiresAt).getTime() <= Date.now()) {
        localStorage.removeItem(STORAGE_KEY);
        return null;
      }
      return session;
    } catch {
      return null;
    }
  }
}
