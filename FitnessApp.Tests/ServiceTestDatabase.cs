using FitnessApp.Data;
using FitnessApp.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Tests;

// The services use DbContext directly. SQLite exercises their relational queries and
// transactions without mocking EF query providers or contacting PostgreSQL.
internal sealed class ServiceTestDatabase : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public AppDbContext Db { get; }

    public ServiceTestDatabase()
    {
        connection.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        Db.Database.EnsureCreated();
    }

    public Customerpass AddPass(int remainingVisits = 3, DateOnly? expires = null)
    {
        var pass = new Customerpass
        {
            Customer = new User { Email = "member@example.test", Fullname = "Test member", Password = "stored-hash" },
            Passtype = new Gympass { Passname = "Basic", Price = 30m, Maxvisits = 10, Validitydays = 30 },
            Purchasedate = new DateOnly(2000, 1, 1),
            Expirydate = expires ?? DateOnly.MaxValue,
            Remainingvisits = remainingVisits
        };
        Db.Customerpasses.Add(pass);
        Db.SaveChanges();
        return pass;
    }

    public void Dispose()
    {
        Db.Dispose();
        connection.Dispose();
    }
}
