using EventPass.Server.Controllers;
using EventPass.Server.Data;
using EventPass.Server.Dtos;
using EventPass.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace EventPass.Tests;

public class UsersControllerTests
{
    // Baza de date are un Admin (id 1) și un Client (id 2); controllerul e apelat ca Adminul
    private static (AppDbContext Db, UsersController Controller) Setup()
    {
        var db = TestHelpers.CreateDb();
        db.Users.AddRange(
            new User { Id = 1, Email = "admin@example.com", Role = UserRole.Admin },
            new User { Id = 2, Email = "client@example.com", Role = UserRole.Client });
        db.SaveChanges();

        var controller = new UsersController(db);
        TestHelpers.LoginAs(controller, userId: 1, role: "Admin");
        return (db, controller);
    }

    [Fact]
    public async Task ChangeRole_AdminulFaceUnClientOrganizer()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.ChangeRole(2, new ChangeRoleRequest("Organizer"));

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(UserRole.Organizer, db.Users.Find(2)!.Role);
    }

    [Fact]
    public async Task ChangeRole_RolInvalid_Returneaza400()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.ChangeRole(2, new ChangeRoleRequest("Sef"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(UserRole.Client, db.Users.Find(2)!.Role);
    }

    [Fact]
    public async Task ChangeRole_RolCaNumar_Returneaza400()
    {
        var (db, controller) = Setup();
        using var _ = db;

        // "99" s-ar putea converti în enum, dar nu e un rol existent
        var result = await controller.ChangeRole(2, new ChangeRoleRequest("99"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ChangeRole_PropriulRol_Returneaza400()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.ChangeRole(1, new ChangeRoleRequest("Client"));

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(UserRole.Admin, db.Users.Find(1)!.Role);
    }

    [Fact]
    public async Task ChangeRole_UtilizatorInexistent_Returneaza404()
    {
        var (db, controller) = Setup();
        using var _ = db;

        var result = await controller.ChangeRole(99, new ChangeRoleRequest("Staff"));

        Assert.IsType<NotFoundResult>(result.Result);
    }
}
