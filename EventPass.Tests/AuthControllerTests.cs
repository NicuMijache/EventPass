using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Tests;

public class AuthControllerTests
{
    private static AuthController CreateController(AppDbContext db) =>
        new(db, new PasswordHasher<User>(), TestHelpers.CreateTokenService());

    private static RegisterRequest Register(string email = "Ion@Example.com") =>
        new(email, "Parola123!", "Ion Ionescu");

    [Fact]
    public async Task Register_CreeazaContCuRolulClient()
    {
        using var db = TestHelpers.CreateDb();

        var result = await CreateController(db).Register(Register());

        var response = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal("Client", response.User.Role);
        Assert.False(string.IsNullOrEmpty(response.Token));
        Assert.Equal(UserRole.Client, db.Users.Single().Role);
    }

    [Fact]
    public async Task Register_SalveazaHashulNuParolaInClar()
    {
        using var db = TestHelpers.CreateDb();

        await CreateController(db).Register(Register());

        var user = db.Users.Single();
        Assert.NotEqual("Parola123!", user.PasswordHash);
        Assert.DoesNotContain("Parola123!", user.PasswordHash);
    }

    [Fact]
    public async Task Register_SalveazaEmailulCuLitereMici()
    {
        using var db = TestHelpers.CreateDb();

        await CreateController(db).Register(Register("  Ion@Example.COM "));

        Assert.Equal("ion@example.com", db.Users.Single().Email);
    }

    [Fact]
    public async Task Register_EmailExistent_Returneaza409()
    {
        using var db = TestHelpers.CreateDb();
        var controller = CreateController(db);
        await controller.Register(Register("ion@example.com"));

        var result = await controller.Register(Register("ION@example.com"));

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Single(db.Users);
    }

    [Fact]
    public async Task Login_CuParolaCorecta_ReturneazaToken()
    {
        using var db = TestHelpers.CreateDb();
        var controller = CreateController(db);
        await controller.Register(Register());

        var result = await controller.Login(new LoginRequest("ion@example.com", "Parola123!"));

        var response = Assert.IsType<AuthResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(string.IsNullOrEmpty(response.Token));
    }

    [Fact]
    public async Task Login_CuParolaGresita_Returneaza401()
    {
        using var db = TestHelpers.CreateDb();
        var controller = CreateController(db);
        await controller.Register(Register());

        var result = await controller.Login(new LoginRequest("ion@example.com", "ParolaGresita"));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task Login_CuEmailInexistent_Returneaza401()
    {
        using var db = TestHelpers.CreateDb();

        var result = await CreateController(db).Login(new LoginRequest("nimeni@example.com", "Parola123!"));

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }
}
