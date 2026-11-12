using FitnessApp.DTO.CheckinDTOs;
using FitnessApp.Models;
using FitnessApp.Service;
using FitnessApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FitnessApp.Tests;

public sealed class CheckinServiceTests : IDisposable
{
    private readonly ServiceTestDatabase database = new();

    [Fact]
    public async Task Create_with_valid_pass_records_trainer_and_consumes_exactly_one_visit()
    {
        // Arrange
        var pass = database.AddPass();
        var trainer = new Trainer { Fullname = "Trainer", Phone = "000", Specialty = "Strength" };
        database.Db.Trainers.Add(trainer);
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CheckinService(database.Db);
        var before = ReceptionTime.Now;

        // Act
        var id = await service.CreateAsync(new CheckinEditDTO
        {
            CustomerPassId = pass.Customerpassid, TrainerId = trainer.Trainerid
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(id);
        database.Db.ChangeTracker.Clear();
        var visit = Assert.Single(await database.Db.Checkins.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(id, visit.Checkinid);
        Assert.Equal(pass.Customerpassid, visit.Customerpassid);
        Assert.Equal(trainer.Trainerid, visit.Trainerid);
        Assert.InRange(visit.Checkindatetime, before, ReceptionTime.Now);
        Assert.Equal(2, (await database.Db.Customerpasses.SingleAsync(TestContext.Current.CancellationToken)).Remainingvisits);
    }

    [Fact]
    public async Task Create_with_nonexistent_pass_returns_null_without_recording_a_visit()
    {
        var service = new CheckinService(database.Db);

        var id = await service.CreateAsync(new CheckinEditDTO { CustomerPassId = 123 }, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Empty(database.Db.Checkins);
    }

    [Fact]
    public async Task Create_with_expired_pass_preserves_remaining_visits()
    {
        var pass = database.AddPass(expires: DateOnly.MinValue);
        var service = new CheckinService(database.Db);

        var id = await service.CreateAsync(new CheckinEditDTO { CustomerPassId = pass.Customerpassid }, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Empty(database.Db.Checkins);
        Assert.Equal(3, pass.Remainingvisits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Create_with_no_remaining_visits_does_not_create_or_consume_a_visit(int visits)
    {
        var pass = database.AddPass(remainingVisits: visits);
        var service = new CheckinService(database.Db);

        var id = await service.CreateAsync(new CheckinEditDTO { CustomerPassId = pass.Customerpassid }, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Empty(database.Db.Checkins);
        Assert.Equal(visits, pass.Remainingvisits);
    }

    [Fact]
    public async Task Create_when_already_checked_in_today_preserves_pass_and_existing_visits()
    {
        var pass = database.AddPass();
        var today = ReceptionTime.Now.Date;
        // Include adjacent dates so a midnight rollover during the test cannot change its outcome.
        database.Db.Checkins.AddRange(Enumerable.Range(-1, 3).Select(offset => new Checkin
        {
            Customerpassid = pass.Customerpassid, Checkindatetime = today.AddDays(offset).AddHours(12)
        }));
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CheckinService(database.Db);

        var id = await service.CreateAsync(new CheckinEditDTO { CustomerPassId = pass.Customerpassid }, TestContext.Current.CancellationToken);

        Assert.Null(id);
        Assert.Equal(3, await database.Db.Checkins.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(3, pass.Remainingvisits);
    }

    [Fact]
    public async Task Create_allows_a_new_visit_when_the_previous_visit_was_on_an_earlier_day()
    {
        var pass = database.AddPass();
        database.Db.Checkins.Add(new Checkin
        {
            Customerpassid = pass.Customerpassid, Checkindatetime = ReceptionTime.Now.Date.AddDays(-2)
        });
        await database.Db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var service = new CheckinService(database.Db);

        var id = await service.CreateAsync(new CheckinEditDTO { CustomerPassId = pass.Customerpassid }, TestContext.Current.CancellationToken);

        Assert.NotNull(id);
        Assert.Equal(2, await database.Db.Checkins.CountAsync(TestContext.Current.CancellationToken));
        Assert.Equal(2, pass.Remainingvisits);
    }

    [Fact]
    public void Delete_refunds_one_visit_and_repeated_deletion_cannot_refund_again()
    {
        var pass = database.AddPass(remainingVisits: 2);
        var visit = new Checkin { Customerpassid = pass.Customerpassid, Checkindatetime = new DateTime(2020, 1, 1) };
        database.Db.Checkins.Add(visit);
        database.Db.SaveChanges();
        var service = new CheckinService(database.Db);

        var deleted = service.Delete(visit.Checkinid);
        var deletedAgain = service.Delete(visit.Checkinid);

        Assert.True(deleted);
        Assert.False(deletedAgain);
        Assert.Empty(database.Db.Checkins);
        database.Db.ChangeTracker.Clear();
        Assert.Equal(3, database.Db.Customerpasses.Single().Remainingvisits);
    }

    [Fact]
    public void Update_with_nonexistent_checkin_returns_false_without_changing_pass()
    {
        var pass = database.AddPass();
        var service = new CheckinService(database.Db);

        var updated = service.Update(new CheckinEditDTO { CheckinId = 123, CustomerPassId = pass.Customerpassid });

        Assert.False(updated);
        Assert.Empty(database.Db.Checkins);
        Assert.Equal(3, pass.Remainingvisits);
    }

    public void Dispose() => database.Dispose();
}
