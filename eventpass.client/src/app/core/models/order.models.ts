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

export interface Order {
  id: number;
  status: OrderStatus;
  totalAmount: number;
  createdAt: string;
  expiresAt: string | null;
  paidAt: string | null;
  event: OrderEvent;
  items: OrderLine[];
}

// Textele afișate pentru fiecare stare
export const ORDER_STATUS_LABELS: Record<OrderStatus, string> = {
  Pending: 'Rezervat – în așteptarea plății',
  Paid: 'Plătit',
  Cancelled: 'Anulat',
  Refunded: 'Rambursat',
};
