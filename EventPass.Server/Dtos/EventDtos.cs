using System.ComponentModel.DataAnnotations;

namespace EventPass.Server.Dtos;

// Folosit atât la creare, cât și la editare (aceleași câmpuri)
public record SaveEventRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string? Description,
    [Required, MaxLength(300)] string Location,
    [Required] DateTime StartsAt);

// Folosit la adăugarea și la editarea unui tip de bilet
public record SaveTicketTypeRequest(
    [Required, MaxLength(100)] string Name,
    [Range(0, 100000)] decimal Price,
    [Range(1, 1000000)] int Capacity);

public record TicketTypeDto(int Id, string Name, decimal Price, int Capacity, int Sold, int Available);

public record EventSummaryDto(
    int Id,
    string Title,
    string Location,
    DateTime StartsAt,
    bool IsPublished,
    int TicketTypesCount);

public record EventDetailDto(
    int Id,
    string Title,
    string Description,
    string Location,
    DateTime StartsAt,
    bool IsPublished,
    DateTime CreatedAt,
    List<TicketTypeDto> TicketTypes);
