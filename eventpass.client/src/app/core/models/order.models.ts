// Tipurile care corespund DTO-urilor din OrderDtos.cs

export type OrderStatus = 'Pending' | 'Paid' | 'Cancelled' | 'Refunded';

export interface OrderItemRequest {
  ticketTypeId: number;
  quantity: number;
}

export interface CreateOrderRequest {
  eventId: number;
  items: OrderItemRequest[];
}

export interface OrderEvent {
  id: number;
  title: string;
  venue: string;
  city: string;
  startsAt: string;
  imageUrl: string | null;
}

export interface OrderLine {
  ticketTypeId: number;
  ticketTypeName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

// Un bilet emis după plată; token-ul va fi în codul QR
export interface Ticket {
  id: number;
  token: string;
  ticketTypeName: string;
  isUsed: boolean;
}

export interface Order {
  id: number;
  status: OrderStatus;
  totalAmount: number;
  createdAt: string;
  expiresAt: string | null;
  paidAt: string | null;
  event: OrderEvent;
  items: OrderLine[];
  tickets: Ticket[];
  // Când s-au trimis biletele pe email (null = încă nu)
  ticketsEmailedAt: string | null;
}

// Textele afișate pentru fiecare stare
export const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
  Pending: 'Rezervat – în așteptarea plății',
  Paid: 'Plătit',
  Cancelled: 'Anulat',
  Refunded: 'Rambursat',
};
