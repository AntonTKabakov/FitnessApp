using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Tests;

public sealed class CustomerPassServiceTests : IDisposable
{
    private readonly ServiceTestDatabase database = new();

    [Fact]
    public async Task Purchase_assigns_catalog_allowance_and_validity_to_the_correct_customer()
    {
        var customer = new User { Email = "buyer@example.test", Fullname = "Buyer", Password = "stored-hash" };
        var type = new Gympass { Passname = "Monthly", Price = 25m, Maxvisits = 12, Validitydays = 30 };
        database.Db.AddRange(customer, type);
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CustomerPassService(database.Db);
        var before = ReceptionTime.Today;

        var id = await service.PurchaseAsync(customer.Id, type.Passtypeid, TestContext.Current.CancellationToken);

        Assert.NotNull(id);
        database.Db.ChangeTracker.Clear();
        var pass = Assert.Single(await database.Db.Customerpasses.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(id, pass.Customerpassid);
        Assert.Equal(customer.Id, pass.Customerid);
        Assert.Equal(type.Passtypeid, pass.Passtypeid);
        Assert.Equal(12, pass.Remainingvisits);
        Assert.InRange(pass.Purchasedate, before, ReceptionTime.Today);
        Assert.Equal(pass.Purchasedate.AddDays(30), pass.Expirydate);
    }

    [Fact]
    public async Task Purchase_with_missing_customer_creates_no_membership()
    {
        var type = new Gympass { Passname = "Monthly", Price = 25m, Maxvisits = 12, Validitydays = 30 };
        database.Db.Gympasses.Add(type);
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CustomerPassService(database.Db);

        var id = await service.PurchaseAsync(123, type.Passtypeid, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Empty(database.Db.Customerpasses);
    }

    [Fact]
    public async Task Purchase_with_missing_catalog_pass_creates_no_membership()
    {
        var customer = new User { Email = "buyer@example.test", Password = "stored-hash" };
        database.Db.Users.Add(customer);
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CustomerPassService(database.Db);

        var id = await service.PurchaseAsync(customer.Id, 123, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Empty(database.Db.Customerpasses);
    }

    [Theory]
    [InlineData(0, 30, 25)]
    [InlineData(-1, 30, 25)]
    [InlineData(12, 0, 25)]
    [InlineData(12, -1, 25)]
    [InlineData(12, 30, -1)]
    [InlineData(12, int.MaxValue, 25)]
    public async Task Purchase_rejects_invalid_catalog_values_without_creating_membership(int visits, int days, int price)
    {
        var customer = new User { Email = "buyer@example.test", Password = "stored-hash" };
        var type = new Gympass { Passname = "Invalid", Price = price, Maxvisits = visits, Validitydays = days };
        database.Db.AddRange(customer, type);
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CustomerPassService(database.Db);

        var id = await service.PurchaseAsync(customer.Id, type.Passtypeid, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Empty(database.Db.Customerpasses);
    }

    public void Dispose() => database.Dispose();
}
