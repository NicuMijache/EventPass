// Tipurile TypeScript care corespund DTO-urilor din backend (EventDtos.cs)

export interface PublicEventCard {
  id: number;
  title: string;
  venue: string;
  city: string;
  startsAt: string;
  imageUrl: string | null;
  minPrice: number | null;
  soldOut: boolean;
}

export interface PublicTicketType {
  id: number;
  name: string;
  price: number;
  available: number;
}

export interface PublicEventDetail {
  id: number;
  title: string;
  description: string;
  venue: string;
  city: string;
  startsAt: string;
  imageUrl: string | null;
  ticketTypes: PublicTicketType[];
}

export interface FilterOption {
  name: string;
  count: number;
}

export interface VenueOption extends FilterOption {
  city: string;
}

export interface EventFilters {
  cities: FilterOption[];
  venues: VenueOption[];
}

// Ce trimitem la /api/events ca parametri de căutare
export interface EventQuery {
  cities?: string[];
  venues?: string[];
  search?: string;
}
