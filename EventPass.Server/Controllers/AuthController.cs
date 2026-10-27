using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using EventPass.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Server.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly TokenService _tokens;

    public AuthController(AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
    }

    // Cont nou; rolul este mereu Client (Organizer/Staff le dă doar Adminul)
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var email = NormalizeEmail(request.Email);
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "Există deja un cont cu acest email." });

        var user = new User
        {
            Email = email,
            FullName = request.FullName.Trim(),
            Role = UserRole.Client
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(CreateResponse(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Email == email);

        // Același mesaj pentru email inexistent și parolă greșită,
        // ca să nu dezvăluim ce emailuri au cont
        if (user is null ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Email sau parolă incorectă." });

        return Ok(CreateResponse(user));
    }

    // Datele utilizatorului logat; fără token valid răspunde 401
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await _db.Users.FindAsync(User.GetUserId());
        if (user is null)
            return Unauthorized();

        return Ok(new UserDto(user.Id, user.Email, user.FullName, user.Role.ToString()));
    }

    private AuthResponse CreateResponse(User user)
    {
        var (token, expiresAt) = _tokens.CreateToken(user);
        return new AuthResponse(token, expiresAt,
            new UserDto(user.Id, user.Email, user.FullName, user.Role.ToString()));
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
