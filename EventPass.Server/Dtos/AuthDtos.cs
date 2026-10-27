using System.ComponentModel.DataAnnotations;

namespace EventPass.Server.Dtos;

// DTO = obiectele primite/trimise prin API, separate de entitățile din baza de date,
// ca să nu expunem niciodată câmpuri ca PasswordHash

public record RegisterRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(8), MaxLength(100)] string Password,
    [Required, MaxLength(200)] string FullName);

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserDto(int Id, string Email, string FullName, string Role);

public record AuthResponse(string Token, DateTime ExpiresAt, UserDto User);

// Rolul nou, ca text: "Client", "Organizer", "Staff" sau "Admin"
public record ChangeRoleRequest([Required] string Role);
