using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Server.Controllers;

// Tot controllerul e accesibil doar Adminului
[ApiController]
[Route("api/users")]
[Authorize(Roles = nameof(UserRole.Admin))]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPut("{id:int}/role")]
    public async Task<ActionResult<UserDto>> ChangeRole(int id, ChangeRoleRequest request)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: true, out var role) ||
            !Enum.IsDefined(role))
            return BadRequest(new { message = $"Rol invalid. Valori permise: {string.Join(", ", Enum.GetNames<UserRole>())}." });

        // Adminul nu își poate schimba propriul rol, ca să nu rămână aplicația fără Admin
        if (id == User.GetUserId())
            return BadRequest(new { message = "Nu îți poți schimba propriul rol." });

        var user = await _db.Users.FindAsync(id);
        if (user is null)
            return NotFound();

        user.Role = role;
        await _db.SaveChangesAsync();

        // Rolul nou apare în token abia la următorul login
        return Ok(new UserDto(user.Id, user.Email, user.FullName, user.Role.ToString()));
    }
}
