using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Tipurile de bilete (și stocul lor) pentru un eveniment al organizatorului logat
[ApiController]
[Route("api/organizer/events/{eventId:int}/ticket-types")]
[Authorize(Roles = nameof(UserRole.Organizer))]
public class OrganizerTicketTypesController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrganizerTicketTypesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<TicketTypeDto>> Create(int eventId, SaveTicketTypeRequest request)
    {
        var ev = await FindMyEventAsync(eventId);
        if (ev is null)
            return NotFound();

        var name = request.Name.Trim();
        if (HasNameConflict(ev, name, exceptId: null))
            return Conflict(new { message = $"Există deja un tip de bilet numit \"{name}\" la acest eveniment." });

        var ticketType = new TicketType
        {
            Name = name,
            Price = request.Price,
            Capacity = request.Capacity,
            EventId = ev.Id
        };

        _db.TicketTypes.Add(ticketType);
        await _db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(OrganizerEventsController.Get),
            "OrganizerEvents",
            new { id = ev.Id },
            ToDto(ticketType));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TicketTypeDto>> Update(int eventId, int id, SaveTicketTypeRequest request)
    {
        var ev = await FindMyEventAsync(eventId);
        var ticketType = ev?.TicketTypes.SingleOrDefault(t => t.Id == id);
        if (ev is null || ticketType is null)
            return NotFound();

        var name = request.Name.Trim();
        if (HasNameConflict(ev, name, exceptId: id))
            return Conflict(new { message = $"Există deja un tip de bilet numit \"{name}\" la acest eveniment." });

        // Nu putem avea mai puține locuri decât bilete deja vândute
        if (request.Capacity < ticketType.Sold)
            return BadRequest(new { message = $"Capacitatea nu poate fi sub numărul de bilete vândute ({ticketType.Sold})." });

        // Schimbarea prețului nu afectează comenzile existente (OrderItem păstrează UnitPrice)
        ticketType.Name = name;
        ticketType.Price = request.Price;
        ticketType.Capacity = request.Capacity;
        await _db.SaveChangesAsync();

        return Ok(ToDto(ticketType));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int eventId, int id)
    {
        var ev = await FindMyEventAsync(eventId);
        var ticketType = ev?.TicketTypes.SingleOrDefault(t => t.Id == id);
        if (ev is null || ticketType is null)
            return NotFound();

        if (await _db.OrderItems.AnyAsync(i => i.TicketTypeId == id))
            return Conflict(new { message = "Tipul de bilet apare în comenzi și nu poate fi șters." });

        // Un eveniment publicat trebuie să aibă mereu ce vinde
        if (ev.IsPublished && ev.TicketTypes.Count == 1)
            return BadRequest(new { message = "Evenimentul e publicat și ar rămâne fără bilete. Retrage-l din vânzare întâi." });

        _db.TicketTypes.Remove(ticketType);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Task<Event?> FindMyEventAsync(int eventId)
    {
        var userId = User.GetUserId();
        return _db.Events
            .Include(e => e.TicketTypes)
            .SingleOrDefaultAsync(e => e.Id == eventId && e.OrganizerId == userId);
    }

    private static bool HasNameConflict(Event ev, string name, int? exceptId) =>
        ev.TicketTypes.Any(t => t.Id != exceptId &&
                                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static TicketTypeDto ToDto(TicketType t) =>
        new(t.Id, t.Name, t.Price, t.Capacity, t.Sold, t.Capacity - t.Sold);
}
