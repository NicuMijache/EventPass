using System.ComponentModel.DataAnnotations;

namespace EventPass.Server.Dtos;

// Ce trimite clientul: doar CE vrea și CÂT. Prețul îl calculează serverul.
public record OrderItemRequest(int TicketTypeId, [Range(1, 10)] int Quantity);

public record CreateOrderRequest(int EventId, [Required, MinLength(1)] List<OrderItemRequest> Items);

public record OrderEventDto(int Id, string Title, string Venue, string City, DateTime StartsAt, string? ImageUrl);

public record OrderLineDto(int TicketTypeId, string TicketTypeName, int Quantity, decimal UnitPrice, decimal LineTotal);

// Un bilet emis după plată. Token-ul e cel din codul QR; îl vede doar proprietarul comenzii.
public record TicketDto(int Id, Guid Token, string TicketTypeName, bool IsUsed);

// Adresa paginii de plată Stripe, unde frontend-ul trimite browserul
public record CheckoutResponse(string Url);

public record OrderDto(
    int Id,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? PaidAt,
    OrderEventDto Event,
    List<OrderLineDto> Items,
    List<TicketDto> Tickets,
    // Când s-au trimis biletele pe email (null = încă nu)
    DateTime? TicketsEmailedAt);

// Scanarea unui bilet la intrare (#17): textul citit din QR sau codul scurt tastat
public record ValidateTicketRequest([Required, MaxLength(64)] string Code);

// Răspunsul scanării. Result: Valid, AlreadyUsed, NotFound, NotPaid, OtherOrganizer, Ambiguous, InvalidCode.
// Detaliile biletului (eveniment, tip, nume) apar doar când biletul a fost găsit.
public record TicketValidationDto(
    string Result,
    string Message,
    string? ShortCode,
    string? EventTitle,
    DateTime? EventStartsAt,
    string? TicketTypeName,
    string? HolderName,
    // Când a fost folosit: acum (Valid) sau la prima scanare (AlreadyUsed)
    DateTime? UsedAt);
