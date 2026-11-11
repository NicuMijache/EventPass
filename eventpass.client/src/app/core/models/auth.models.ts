// Tipurile care corespund DTO-urilor din AuthDtos.cs

export type UserRole = 'Client' | 'Organizer' | 'Staff' | 'Admin';

// Cine are voie să scaneze bilete la intrare (clienții nu)
export const SCANNER_ROLES: readonly UserRole[] = ['Staff', 'Organizer', 'Admin'];
// Cine are panoul organizatorului (#20); API-ul îl permite tot doar organizatorilor
export const ORGANIZER_ROLES: readonly UserRole[] = ['Organizer'];

export interface User {
  id: number;
  email: string;
  fullName: string;
  role: UserRole;
}

export interface AuthResponse {
  token: string;
  expiresAt: string;
  user: User;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest extends LoginRequest {
  fullName: string;
}
