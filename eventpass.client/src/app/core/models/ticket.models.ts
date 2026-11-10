// Răspunsul serverului la scanarea unui bilet (POST /api/tickets/validate)
export type TicketValidationResult =
  | 'Valid' // bilet bun, tocmai marcat ca folosit: persoana intră
  | 'AlreadyUsed' // a mai fost scanat
  | 'NotFound'
  | 'NotPaid'
  | 'OtherOrganizer'
  | 'Ambiguous'
  | 'InvalidCode';

export interface TicketValidation {
  result: TicketValidationResult;
  message: string;
  shortCode: string | null;
  eventTitle: string | null;
  eventStartsAt: string | null;
  ticketTypeName: string | null;
  holderName: string | null;
  usedAt: string | null;
}
