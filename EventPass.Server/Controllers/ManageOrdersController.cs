using EventPass.Server.Data;
using EventPass.Server.Models;
using EventPass.Server.Services;
using EventPass.Server.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

// Comenzile văzute de organizator / admin (#19). Ex.: un eveniment se anulează și se dau banii înapoi.
// Spre deosebire de client, nu au termenul de 24 de ore; regula „niciun bilet folosit” rămâne.
[ApiController]
[Route("api/manage/orders")]
[Authorize(Roles = $"{nameof(UserRole.Organizer)},{nameof(UserRole.Admin)}")]
public class ManageOrdersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OrderRefundService _refunds;

    public ManageOrdersController(AppDbContext db, OrderRefundService refunds)
    {
        _db = db;
        _refunds = refunds;
    }

    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, CancellationToken ct)
    {
        // Organizatorul poate rambursa doar comenzile evenimentelor lui; pentru restul, 404 (nu aflăm nici că există)
        var organizerId = await _db.Orders
            .Where(o => o.Id == id)
            .Select(o => (int?)o.Items.First().TicketType.Event.OrganizerId)
            .SingleOrDefaultAsync(ct);
        if (organizerId is null || (!User.IsInRole(nameof(UserRole.Admin)) && organizerId != User.GetUserId()))
            return NotFound();

        var outcome = await _refunds.RefundAsync(id, ct);
        return outcome switch
        {
            RefundOutcome.NotPaid => Conflict(new { message = "Doar o comandă plătită poate fi rambursată." }),
            RefundOutcome.TicketsUsed => Conflict(new { message = "Un bilet din comandă a fost deja folosit la intrare." }),
            RefundOutcome.InProgress => Accepted(new { orderId = id, status = nameof(OrderStatus.RefundPending),
                message = "Biletele sunt anulate; rambursarea la Stripe se reîncearcă automat." }),
            _ => Ok(new { orderId = id, status = nameof(OrderStatus.Refunded) }),
        };
    }
}
