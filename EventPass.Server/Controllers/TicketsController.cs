using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Server.Controllers;

// Validarea biletelor la intrare. Doar personalul are voie: un client care ar putea apela endpoint-ul
// și-ar putea „consuma” singur biletele (sau pe ale altora, dacă le-ar ghici codul).
[ApiController]
[Route("api/tickets")]
[Authorize(Roles = $"{nameof(UserRole.Staff)},{nameof(UserRole.Organizer)},{nameof(UserRole.Admin)}")]
public class TicketsController : ControllerBase
{
    private readonly TicketValidationService _validation;

    public TicketsController(TicketValidationService validation) => _validation = validation;

    // Răspundem mereu cu 200 și rezultatul în corp (Valid, AlreadyUsed, NotFound...): scanarea a reușit,
    // doar biletul poate fi bun sau nu. Codurile de eroare (4xx) rămân pentru cereri greșite
    // (corp lipsă → 400, fără login → 401, rol nepermis → 403).
    // POST, nu GET: cererea SCHIMBĂ ceva (marchează biletul ca folosit).
    [HttpPost("validate")]
    public async Task<ActionResult<TicketValidationDto>> Validate(ValidateTicketRequest request, CancellationToken ct)
    {
        var userId = User.GetUserId();
        int? organizerId = User.IsInRole(nameof(UserRole.Organizer)) ? userId : null;

        return Ok(await _validation.ValidateAsync(request.Code, userId, organizerId, ct));
    }
}
