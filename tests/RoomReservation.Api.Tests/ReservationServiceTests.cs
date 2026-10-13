using Microsoft.Extensions.Logging.Abstractions;
using RoomReservation.Api.Data;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;
using Xunit;

namespace RoomReservation.Api.Tests;

/// <summary>
/// Behaviour of the write operations: rules are enforced before saving, and the
/// cancel / end-early actions produce the state the rest of the app depends on.
/// </summary>
public class ReservationServiceTests
{
    private static readonly DateTime Now = TestContextFactory.Now;

    private static (ReservationService Service, AppDbContext Db, FixedTimeProvider Clock) Create(
        BookingSettings? settings = null,
        FixedTimeProvider? clock = null)
    {
        var db = TestContextFactory.CreateContext();
        TestContextFactory.Seed(db);

        var time = clock ?? new FixedTimeProvider(Now);
        var effectiveSettings = settings ?? TestContextFactory.DefaultSettings();
        var validator = new BookingRuleValidator(db, time, effectiveSettings);
        var service = new ReservationService(db, validator, time, NullLogger<ReservationService>.Instance);

        return (service, db, time);
    }

    [Fact]
    public async Task Create_ValidReservation_IsPersisted()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var result = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Planning", Now.AddDays(1), Now.AddDays(1).AddHours(2), 4, "Notes"));

        Assert.True(result.Succeeded);
        Assert.Equal(Results.Created, result.StatusCode);
        Assert.Equal(1, db.Reservations.Count());
        Assert.Equal("Planning", result.Reservation!.Title);
        Assert.Equal("Booked", result.Reservation.Status);
    }

    [Fact]
    public async Task Create_BreakingARule_IsRejectedWithTheRuleCode()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();     // capacity 6
        var user = db.Users.First();

        var result = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Too many", Now.AddDays(1), Now.AddDays(1).AddHours(1), 99, null));

        Assert.False(result.Succeeded);
        Assert.Equal(Results.BadRequest, result.StatusCode);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.CapacityExceeded);
        Assert.Empty(db.Reservations);
    }

    [Fact]
    public async Task Create_OverlappingWithCancelledReservation_Succeeds()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();
        var other = db.Users.Skip(1).First();

        db.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            OrganizerId = user.Id,
            Title = "Cancelled",
            Start = Now.AddDays(1),
            End = Now.AddDays(1).AddHours(2),
            Attendees = 4,
            Status = ReservationStatus.Cancelled
        });
        await db.SaveChangesAsync();

        var result = await service.CreateAsync(new CreateReservationRequest(
            room.Id, other.Id, "Replacement", Now.AddDays(1), Now.AddDays(1).AddHours(2), 4, null));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Update_ValidChange_IsApplied()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var created = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Original", Now.AddDays(1), Now.AddDays(1).AddHours(1), 3, null));

        var result = await service.UpdateAsync(created.Reservation!.Id, new UpdateReservationRequest(
            room.Id, user.Id, "Renamed", Now.AddDays(2), Now.AddDays(2).AddHours(3), 5, "Updated"));

        Assert.True(result.Succeeded);
        Assert.Equal("Renamed", result.Reservation!.Title);
        Assert.Equal("Updated", result.Reservation.Notes);
        Assert.Equal(3, result.Reservation.DurationHours);
    }

    [Fact]
    public async Task Update_ToAnInvalidSlot_IsRejectedAndLeavesTheOriginalUntouched()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var created = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Original", Now.AddDays(1), Now.AddDays(1).AddHours(1), 3, null));

        var result = await service.UpdateAsync(created.Reservation!.Id, new UpdateReservationRequest(
            room.Id, user.Id, "Broken", Now.AddDays(-5), Now.AddDays(-5).AddHours(1), 3, null));

        Assert.False(result.Succeeded);
        Assert.True(result.StatusCode == Results.BadRequest);

        var stored = db.Reservations.Single();
        Assert.Equal("Original", stored.Title);
        Assert.Equal(Now.AddDays(1), stored.Start);
    }

    [Fact]
    public async Task Update_CancelledReservation_IsRefused()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var created = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Later cancelled", Now.AddDays(1), Now.AddDays(1).AddHours(1), 3, null));

        await service.CancelAsync(created.Reservation!.Id);

        var result = await service.UpdateAsync(created.Reservation.Id, new UpdateReservationRequest(
            room.Id, user.Id, "Try again", Now.AddDays(2), Now.AddDays(2).AddHours(1), 3, null));

        Assert.False(result.Succeeded);
        Assert.Equal(Results.Conflict, result.StatusCode);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.ReservationNotModifiable);
    }

    [Fact]
    public async Task Cancel_SetsStatusAndTimestamp()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var created = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Cancel me", Now.AddDays(1), Now.AddDays(1).AddHours(1), 3, null));

        var result = await service.CancelAsync(created.Reservation!.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("Cancelled", result.Reservation!.Status);
        Assert.NotNull(result.Reservation.CancelledAt);
        Assert.Equal(Now, result.Reservation.CancelledAt!.Value);
    }

    [Fact]
    public async Task Cancel_Twice_IsRefused()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var created = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Cancel me twice", Now.AddDays(1), Now.AddDays(1).AddHours(1), 3, null));

        await service.CancelAsync(created.Reservation!.Id);
        var second = await service.CancelAsync(created.Reservation.Id);

        Assert.False(second.Succeeded);
        Assert.Equal(Results.Conflict, second.StatusCode);
        Assert.Contains(second.Errors, e => e.Code == ErrorCodes.ReservationNotCancellable);
    }

    [Fact]
    public async Task Cancel_UnknownReservation_ReturnsNotFound()
    {
        var (service, _, _) = Create();

        var result = await service.CancelAsync(4242);

        Assert.False(result.Succeeded);
        Assert.Equal(Results.NotFound, result.StatusCode);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.ReservationNotFound);
    }

    [Fact]
    public async Task EndEarly_AfterStart_ClosesTheMeetingAndRecordsAttendance()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        // The meeting started an hour ago, so it can be ended now.
        db.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            OrganizerId = user.Id,
            Title = "In progress",
            Start = Now.AddHours(-1),
            End = Now.AddHours(1),
            Attendees = 6,
            Status = ReservationStatus.Booked
        });
        await db.SaveChangesAsync();
        var id = db.Reservations.Single().Id;

        var result = await service.EndEarlyAsync(id, new EndMeetingRequest(2));

        Assert.True(result.Succeeded);
        Assert.Equal("Completed", result.Reservation!.Status);
        Assert.Equal(2, result.Reservation.ActualAttendees);
        Assert.Equal(Now, result.Reservation.CompletedAt!.Value);
    }

    [Fact]
    public async Task EndEarly_BeforeStart_IsRefused()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        var created = await service.CreateAsync(new CreateReservationRequest(
            room.Id, user.Id, "Tomorrow", Now.AddDays(1), Now.AddDays(1).AddHours(1), 3, null));

        var result = await service.EndEarlyAsync(created.Reservation!.Id, new EndMeetingRequest(3));

        Assert.False(result.Succeeded);
        Assert.Equal(Results.Conflict, result.StatusCode);
        Assert.Contains(result.Errors, e => e.Code == ErrorCodes.ReservationNotStarted);
    }

    [Fact]
    public async Task EndEarly_WithMoreAttendeesThanBooked_IsRefused()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        db.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            OrganizerId = user.Id,
            Title = "In progress",
            Start = Now.AddHours(-1),
            End = Now.AddHours(1),
            Attendees = 4,
            Status = ReservationStatus.Booked
        });
        await db.SaveChangesAsync();
        var id = db.Reservations.Single().Id;

        var result = await service.EndEarlyAsync(id, new EndMeetingRequest(9));

        Assert.False(result.Succeeded);
        Assert.Equal(Results.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task EndEarly_WithoutAttendance_StillClosesTheMeeting()
    {
        var (service, db, _) = Create();
        var room = db.Rooms.First();
        var user = db.Users.First();

        db.Reservations.Add(new Reservation
        {
            RoomId = room.Id,
            OrganizerId = user.Id,
            Title = "In progress",
            Start = Now.AddHours(-1),
            End = Now.AddHours(1),
            Attendees = 4,
            Status = ReservationStatus.Booked
        });
        await db.SaveChangesAsync();
        var id = db.Reservations.Single().Id;

        var result = await service.EndEarlyAsync(id, new EndMeetingRequest(null));

        Assert.True(result.Succeeded);
        Assert.Equal("Completed", result.Reservation!.Status);
        Assert.Null(result.Reservation.ActualAttendees);
    }

    [Fact]
    public async Task FinishAsync_TransitionsBookedReservationsWhoseEndHasPassed()
    {
        // Mirrors what the ReservationStatusUpdater background service does on its tick.
        using var db = TestContextFactory.CreateContext();
        var seed = TestContextFactory.Seed(db);

        db.Reservations.Add(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Finished yesterday",
            Start = Now.AddDays(-2),
            End = Now.AddDays(-2).AddHours(1),
            Attendees = 3,
            Status = ReservationStatus.Booked
        });
        db.Reservations.Add(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Still to come",
            Start = Now.AddDays(1),
            End = Now.AddDays(1).AddHours(1),
            Attendees = 3,
            Status = ReservationStatus.Booked
        });
        await db.SaveChangesAsync();

        var finished = db.Reservations.Where(r => r.Status == ReservationStatus.Booked && r.End <= Now).ToList();
        foreach (var reservation in finished)
        {
            reservation.Status = ReservationStatus.Completed;
            reservation.CompletedAt = reservation.End;
        }

        await db.SaveChangesAsync();

        Assert.Equal(1, db.Reservations.Count(r => r.Status == ReservationStatus.Completed));
        Assert.Equal(1, db.Reservations.Count(r => r.Status == ReservationStatus.Booked));
    }

    /// <summary>There is no HttpStatusCode in this layer; the service returns plain ints.</summary>
    private static class Results
    {
        public const int Ok = 200;
        public const int Created = 201;
        public const int BadRequest = 400;
        public const int NotFound = 404;
        public const int Conflict = 409;
    }
}
