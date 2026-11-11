// Panoul organizatorului (#20). Corespund DTO-urilor din DashboardDtos.cs.
import { OrderStatus } from './order.models';

export interface SalesTotals {
  capacity: number;
  ticketsSold: number; // bilete din comenzi plătite
  reserved: number; // rezervări neplătite (încă nu sunt vândute)
  available: number;
  revenue: number;
  validated: number; // bilete scanate la intrare
  refundedOrders: number;
  refundedAmount: number;
}

export interface DashboardEvent {
  id: number;
  title: string;
  venue: string;
  city: string;
  startsAt: string;
  isPublished: boolean;
  totals: SalesTotals;
}

export interface TicketTypeSales {
  id: number;
  name: string;
  price: number;
  capacity: number;
  ticketsSold: number;
  reserved: number;
  available: number;
  revenue: number;
  validated: number;
}

export interface EventSales extends DashboardEvent {
  ticketTypes: TicketTypeSales[];
}

export interface Attendee {
  ticketId: number;
  shortCode: string;
  ticketTypeName: string;
  holderName: string;
  holderEmail: string;
  orderId: number;
  orderStatus: OrderStatus;
  paidAt: string | null;
  isUsed: boolean;
  usedAt: string | null;
  isVoided: boolean;
  canRefund: boolean;
}

export interface AttendeesPage {
  total: number;
  page: number;
  pageSize: number;
  items: Attendee[];
}

export type AttendeeFilter = 'all' | 'validated' | 'notValidated' | 'refunded';

export interface AttendeeQuery {
  q?: string;
  filter?: AttendeeFilter;
  page?: number;
}

// Procent întreg (0–100), sigur la împărțirea cu zero
export function percent(part: number, whole: number): number {
  return whole > 0 ? Math.round((part / whole) * 100) : 0;
}
