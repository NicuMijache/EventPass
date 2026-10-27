using EventPass.Server.Data;
using EventPass.Server.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Pagina publică: oricine (fără login) vede evenimentele publicate care nu au trecut
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _db;

    public EventsController(AppDbContext db)
    {
        _db = db;
    }

    // Filtrele se pot repeta: ?city=Cluj-Napoca&city=Iași&venue=BT Arena&search=jazz
    [HttpGet]
    public async Task<ActionResult<List<PublicEventCardDto>>> GetAll(
        [FromQuery(Name = "city")] string[]? cities,
        [FromQuery(Name = "venue")] string[]? venues,
        [FromQuery] string? search)
    {
        var query = _db.Events.Where(e => e.IsPublished && e.StartsAt > DateTime.UtcNow);

        if (cities is { Length: > 0 })
            query = query.Where(e => cities.Contains(e.City));
        if (venues is { Length: > 0 })
            query = query.Where(e => venues.Contains(e.Venue));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(e =>
                e.Title.ToLower().Contains(term) ||
                e.Venue.ToLower().Contains(term) ||
                e.City.ToLower().Contains(term));
        }

        var events = await query
            .OrderBy(e => e.StartsAt)
            .Select(e => new PublicEventCardDto(
                e.Id,
                e.Title,
                e.Venue,
                e.City,
                e.StartsAt,
                e.ImagePath,
                e.TicketTypes.Min(t => (decimal?)t.Price),
                e.TicketTypes.All(t => t.Sold >= t.Capacity)))
            .ToListAsync();

        return Ok(events);
    }

    // Opțiunile pentru panoul lateral: orașele și locațiile care au evenimente active
    [HttpGet("filters")]
    public async Task<ActionResult<EventFiltersDto>> GetFilters()
    {
        var active = _db.Events.Where(e => e.IsPublished && e.StartsAt > DateTime.UtcNow);

        // Grupăm în baza de date într-un obiect simplu, apoi construim DTO-urile în C#
        // (PostgreSQL nu poate traduce direct constructorul unui record după GroupBy)
        var cities = await active
            .GroupBy(e => e.City)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderBy(c => c.Name)
            .ToListAsync();

        var venues = await active
            .GroupBy(e => new { e.Venue, e.City })
            .Select(g => new { g.Key.Venue, g.Key.City, Count = g.Count() })
            .OrderBy(v => v.Venue)
            .ToListAsync();

        return Ok(new EventFiltersDto(
            cities.Select(c => new FilterOptionDto(c.Name, c.Count)).ToList(),
            venues.Select(v => new VenueOptionDto(v.Venue, v.City, v.Count)).ToList()));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PublicEventDetailDto>> Get(int id)
    {
        // Ciornele nu se văd public: pentru ele răspundem 404, ca și cum n-ar exista
        var ev = await _db.Events
            .Include(e => e.TicketTypes)
            .SingleOrDefaultAsync(e => e.Id == id && e.IsPublished);
        if (ev is null)
            return NotFound();

        return Ok(new PublicEventDetailDto(
            ev.Id,
            ev.Title,
            ev.Description,
            ev.Venue,
            ev.City,
            ev.StartsAt,
            ev.ImagePath,
            ev.TicketTypes
                .OrderBy(t => t.Price)
                .Select(t => new PublicTicketTypeDto(t.Id, t.Name, t.Price, t.Capacity - t.Sold))
                .ToList()));
    }
}
