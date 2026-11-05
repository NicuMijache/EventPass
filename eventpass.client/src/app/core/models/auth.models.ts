// Tipurile care corespund DTO-urilor din AuthDtos.cs

export type UserRole = 'Client' | 'Organizer' | 'Staff' | 'Admin';

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
