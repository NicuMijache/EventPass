namespace EventPass.Server.Dtos;

// Panoul organizatorului (#20). Toate cifrele numără doar comenzile PLĂTITE, în afară de cele marcate altfel.

// Cifrele principale, aceleași pentru un eveniment și pentru lista de evenimente
public record SalesTotalsDto(
    int Capacity,        // locuri în total
    int TicketsSold,     // bilete din comenzi plătite
    int Reserved,        // locuri ținute de rezervări neplătite (încă nu sunt vândute)
    int Available,       // locuri libere acum
    decimal Revenue,     // încasări din comenzile plătite (lei)
    int Validated,       // bilete scanate la intrare
    int RefundedOrders,  // comenzi rambursate (sau cu rambursarea în curs)
    decimal RefundedAmount);

// Un rând din lista „evenimentele mele”
public record DashboardEventDto(
    int Id,
    string Title,
    string Venue,
    string City,
    DateTime StartsAt,
    bool IsPublished,
    SalesTotalsDto Totals);

// O categorie de bilet (ex. VIP) cu vânzările ei
public record TicketTypeSalesDto(
    int Id,
    string Name,
    decimal Price,
    int Capacity,
    int TicketsSold,
    int Reserved,
    int Available,
    decimal Revenue,
    int Validated);

public record EventSalesDto(
    int Id,
    string Title,
    string Venue,
    string City,
    DateTime StartsAt,
    bool IsPublished,
    SalesTotalsDto Totals,
    List<TicketTypeSalesDto> TicketTypes);

// Un bilet emis, cu cine l-a cumpărat (lista participanților)
public record AttendeeDto(
    int TicketId,
    string ShortCode,
    string TicketTypeName,
    string HolderName,
    string HolderEmail,
    int OrderId,
    string OrderStatus,
    DateTime? PaidAt,
    bool IsUsed,
    DateTime? UsedAt,
    bool IsVoided,
    // Organizatorul poate rambursa comanda: e plătită și niciun bilet din ea n-a fost folosit
    bool CanRefund);

public record AttendeesPageDto(int Total, int Page, int PageSize, List<AttendeeDto> Items);
