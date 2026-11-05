using System.ComponentModel.DataAnnotations;

namespace EventPass.Server.Dtos;

// Ce trimite clientul: doar CE vrea și CÂT. Prețul îl calculează serverul.
public record OrderItemRequest(int TicketTypeId, [Range(1, 10)] int Quantity);

public record CreateOrderRequest(int EventId, [Required, MinLength(1)] List<OrderItemRequest> Items);

public record OrderEventDto(int Id, string Title, string Venue, string City, DateTime StartsAt, string? ImageUrl);

public record OrderLineDto(int TicketTypeId, string TicketTypeName, int Quantity, decimal UnitPrice, decimal LineTotal);

public record OrderDto(
    int Id,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? PaidAt,
    OrderEventDto Event,
    List<OrderLineDto> Items);
