using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Tests;

/// <summary>
/// A clock the tests control, so "now" is fixed and rule assertions never depend on
/// the real time of day.
/// </summary>
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTime utcNow) => UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public DateTime UtcNow { get; set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}

/// <summary>Builds an isolated in-memory database plus the seed data each test needs.</summary>
public static class TestContextFactory
{
    /// <summary>A stable "now" for every test: 2026-03-10 09:00 UTC (a Tuesday).</summary>
    public static readonly DateTime Now = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    public static AppDbContext CreateContext(string? name = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    public static BookingSettings DefaultSettings() => new()
    {
        MaxReservationDurationDays = 7,
        BookingHorizonDays = 90,
        UnderusedThreshold = 0.5,
        MinimumMeetingsForUsageVerdict = 3
    };

    /// <summary>Seeds one small and one large active room plus one inactive room and two users.</summary>
    public static (Room Small, Room Large, Room Inactive, User Organizer, User Other) Seed(AppDbContext db)
    {
        var small = new Room
        {
            Name = "Bastion",
            Code = "KA-202",
            Capacity = 6,
            Floor = "2nd floor",
            IsActive = true,
            Equipment = new List<Equipment> { Equipment.TvScreen, Equipment.VideoConference }
        };

        var large = new Room
        {
            Name = "Cascade",
            Code = "KA-305",
            Capacity = 30,
            Floor = "3rd floor",
            IsActive = true,
            Equipment = new List<Equipment> { Equipment.Projector, Equipment.Whiteboard }
        };

        var inactive = new Room
        {
            Name = "Fjord",
            Code = "KA-108",
            Capacity = 8,
            Floor = "1st floor",
            IsActive = false,
            Equipment = new List<Equipment> { Equipment.Projector }
        };

        var organizer = new User { FirstName = "Auste", LastName = "Kazlauskaite", Code = "EMP-0001", Email = "a@example.com", Department = "Engineering" };
        var other = new User { FirstName = "Mantas", LastName = "Petrauskas", Code = "EMP-0002", Email = "m@example.com", Department = "Product" };

        db.Rooms.AddRange(small, large, inactive);
        db.Users.AddRange(organizer, other);
        db.SaveChanges();

        return (small, large, inactive, organizer, other);
    }
}
