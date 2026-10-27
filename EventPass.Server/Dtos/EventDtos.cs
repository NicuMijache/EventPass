using System.ComponentModel.DataAnnotations;

namespace EventPass.Server.Dtos;

// Folosit atât la creare, cât și la editare (aceleași câmpuri)
public record SaveEventRequest(
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string? Description,
    [Required, MaxLength(200)] string Venue,
    [Required, MaxLength(100)] string City,
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
    string Venue,
    string City,
    DateTime StartsAt,
    string? ImageUrl,
    bool IsPublished,
    int TicketTypesCount);

public record EventDetailDto(
    int Id,
    string Title,
    string Description,
    string Venue,
    string City,
    DateTime StartsAt,
    string? ImageUrl,
    bool IsPublished,
    DateTime CreatedAt,
    List<TicketTypeDto> TicketTypes);

// ---- Pagina publică (clienți, fără login) ----

// Un card din grila de pe pagina principală
public record PublicEventCardDto(
    int Id,
    string Title,
    string Venue,
    string City,
    DateTime StartsAt,
    string? ImageUrl,
    decimal? MinPrice,
    bool SoldOut);

public record PublicTicketTypeDto(int Id, string Name, decimal Price, int Available);

public record PublicEventDetailDto(
    int Id,
    string Title,
    string Description,
    string Venue,
    string City,
    DateTime StartsAt,
    string? ImageUrl,
    List<PublicTicketTypeDto> TicketTypes);

// Valorile pentru panoul de filtre, cu numărul de evenimente la fiecare
public record FilterOptionDto(string Name, int Count);

public record VenueOptionDto(string Name, string City, int Count);

public record EventFiltersDto(List<FilterOptionDto> Cities, List<VenueOptionDto> Venues);
