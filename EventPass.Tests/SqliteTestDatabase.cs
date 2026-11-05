using EventPass.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EventPass.Tests;

// O bază de date SQLite reală, ținută în memorie cât trăiește testul.
// Spre deosebire de UseInMemoryDatabase, știe tranzacții, UPDATE condiționat (ExecuteUpdate)
// și verifică regulile din baza de date (ex. Sold <= Capacity) — exact ce testăm la comenzi.
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public SqliteTestDatabase()
    {
        // Conexiunea rămâne deschisă: baza „:memory:” dispare când se închide
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
