using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Evenimentele organizatorului logat. Fiecare organizator vede și modifică
// doar evenimentele lui; pentru ale altora primește 404, ca și cum n-ar exista.
[ApiController]
[Route("api/organizer/events")]
[Authorize(Roles = nameof(UserRole.Organizer))]
public class OrganizerEventsController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrganizerEventsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<EventSummaryDto>>> GetMine()
    {
        var userId = User.GetUserId();
        var events = await _db.Events
            .Where(e => e.OrganizerId == userId)
            .OrderBy(e => e.StartsAt)
            .Select(e => new EventSummaryDto(
                e.Id, e.Title, e.Location, e.StartsAt, e.IsPublished, e.TicketTypes.Count))
            .ToListAsync();

        return Ok(events);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EventDetailDto>> Get(int id)
    {
        var ev = await FindMyEventAsync(id);
        if (ev is null)
            return NotFound();

        return Ok(ToDetail(ev));
    }

    // Evenimentul nou pornește mereu ca ciornă (nepublicat)
    [HttpPost]
    public async Task<ActionResult<EventDetailDto>> Create(SaveEventRequest request)
    {
        var startsAt = ToUtc(request.StartsAt);
        if (startsAt <= DateTime.UtcNow)
            return BadRequest(new { message = "Data evenimentului trebuie să fie în viitor." });

        var ev = new Event
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? string.Empty,
            Location = request.Location.Trim(),
            StartsAt = startsAt,
            OrganizerId = User.GetUserId()
        };

        _db.Events.Add(ev);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = ev.Id }, ToDetail(ev));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EventDetailDto>> Update(int id, SaveEventRequest request)
    {
        var ev = await FindMyEventAsync(id);
        if (ev is null)
            return NotFound();

        var startsAt = ToUtc(request.StartsAt);
        if (startsAt <= DateTime.UtcNow)
            return BadRequest(new { message = "Data evenimentului trebuie să fie în viitor." });

        ev.Title = request.Title.Trim();
        ev.Description = request.Description?.Trim() ?? string.Empty;
        ev.Location = request.Location.Trim();
        ev.StartsAt = startsAt;
        await _db.SaveChangesAsync();

        return Ok(ToDetail(ev));
    }

    [HttpPost("{id:int}/publish")]
    public async Task<ActionResult<EventDetailDto>> Publish(int id)
    {
        var ev = await FindMyEventAsync(id);
        if (ev is null)
            return NotFound();

        if (ev.TicketTypes.Count == 0)
            return BadRequest(new { message = "Adaugă cel puțin un tip de bilet înainte de publicare." });
        if (ev.StartsAt <= DateTime.UtcNow)
            return BadRequest(new { message = "Nu poți publica un eveniment care a trecut." });

        ev.IsPublished = true;
        await _db.SaveChangesAsync();

        return Ok(ToDetail(ev));
    }

    // Retrage evenimentul din vânzare; biletele deja cumpărate rămân valabile
    [HttpPost("{id:int}/unpublish")]
    public async Task<ActionResult<EventDetailDto>> Unpublish(int id)
    {
        var ev = await FindMyEventAsync(id);
        if (ev is null)
            return NotFound();

        ev.IsPublished = false;
        await _db.SaveChangesAsync();

        return Ok(ToDetail(ev));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await FindMyEventAsync(id);
        if (ev is null)
            return NotFound();

        // Dacă există comenzi pe acest eveniment, nu îl ștergem: s-ar pierde
        // biletele clienților. Organizatorul îl poate retrage (unpublish).
        var hasOrders = await _db.OrderItems.AnyAsync(i => i.TicketType.EventId == id);
        if (hasOrders)
            return Conflict(new { message = "Evenimentul are comenzi și nu poate fi șters. Îl poți retrage din vânzare." });

        _db.Events.Remove(ev);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Task<Event?> FindMyEventAsync(int id)
    {
        var userId = User.GetUserId();
        return _db.Events
            .Include(e => e.TicketTypes)
            .SingleOrDefaultAsync(e => e.Id == id && e.OrganizerId == userId);
    }

    // PostgreSQL acceptă doar date în UTC. O dată fără fus orar o considerăm deja UTC.
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static EventDetailDto ToDetail(Event ev) => new(
        ev.Id,
        ev.Title,
        ev.Description,
        ev.Location,
        ev.StartsAt,
        ev.IsPublished,
        ev.CreatedAt,
        ev.TicketTypes
            .OrderBy(t => t.Price)
            .Select(t => new TicketTypeDto(t.Id, t.Name, t.Price, t.Capacity, t.Sold, t.Capacity - t.Sold))
            .ToList());
}
