using RoomReservation.Api.Domain;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;
using Xunit;

namespace RoomReservation.Api.Tests;

/// <summary>
/// Rule by rule coverage of R1-R7, including the boundary cases the task calls out
/// ("exactly 7 days is allowed", "exactly 90 days").
/// </summary>
public class BookingRuleValidatorTests
{
    private static readonly DateTime Now = TestContextFactory.Now;

    private static async Task<List<ValidationError>> ValidateAsync(
        Func<RoomReservation.Api.Data.AppDbContext, Task>? arrange,
        Func<(Room Room, Room Large, Room Inactive, User Organizer, User Other), Reservation> build,
        BookingSettings? settings = null,
        FixedTimeProvider? clock = null)
    {
        using var db = TestContextFactory.CreateContext();
        var seed = TestContextFactory.Seed(db);

        if (arrange is not null)
        {
            await arrange(db);
        }

        var validator = new BookingRuleValidator(
            db,
            clock ?? new FixedTimeProvider(Now),
            settings ?? TestContextFactory.DefaultSettings());

        var candidate = build((seed.Small, seed.Large, seed.Inactive, seed.Organizer, seed.Other));
        return (await validator.ValidateAsync(candidate)).ToList();
    }

    private static bool Has(List<ValidationError> errors, string code) =>
        errors.Any(e => e.Code == code);

    // ---------------------------------------------------------------------
    // R1 - maximum duration: 7 days allowed, more rejected.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R1_Reservation_ExactlySevenDays_IsAllowed()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Week long workshop",
                Start = Now.AddDays(1),
                End = Now.AddDays(8),
                Attendees = 4
            });

        Assert.DoesNotContain(errors, e => e.Code == ErrorCodes.MaxDurationExceeded);
    }

    [Fact]
    public async Task R1_Reservation_LongerThanSevenDays_IsRejected()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Too long",
                Start = Now.AddDays(1),
                End = Now.AddDays(8).AddMinutes(1),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.MaxDurationExceeded));
    }

    [Fact]
    public async Task R1_MaximumDuration_FollowsConfiguration()
    {
        var settings = TestContextFactory.DefaultSettings();
        settings.MaxReservationDurationDays = 2;

        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Three days",
                Start = Now.AddDays(1),
                End = Now.AddDays(4),
                Attendees = 4
            },
            settings);

        Assert.True(Has(errors, ErrorCodes.MaxDurationExceeded));
    }

    // ---------------------------------------------------------------------
    // R2 - no overlapping room bookings.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R2_OverlappingRoomBooking_IsRejected()
    {
        var errors = await ValidateAsync(
            async db =>
            {
                var seed = db.Rooms.ToList();
                db.Reservations.Add(new Reservation
                {
                    RoomId = seed[0].Id,
                    OrganizerId = db.Users.First().Id,
                    Title = "Existing",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4,
                    Status = ReservationStatus.Booked
                });
                await db.SaveChangesAsync();
            },
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Other.Id,
                Title = "Clashing",
                Start = Now.AddHours(3),
                End = Now.AddHours(5),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.RoomDoubleBooked));
    }

    [Fact]
    public async Task R2_BackToBackBookings_AreAllowed()
    {
        var errors = await ValidateAsync(
            async db =>
            {
                var room = db.Rooms.First();
                db.Reservations.Add(new Reservation
                {
                    RoomId = room.Id,
                    OrganizerId = db.Users.First().Id,
                    Title = "Existing",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4
                });
                await db.SaveChangesAsync();
            },
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Other.Id,
                Title = "Immediately after",
                Start = Now.AddHours(4),
                End = Now.AddHours(6),
                Attendees = 4
            });

        Assert.False(Has(errors, ErrorCodes.RoomDoubleBooked));
    }

    [Fact]
    public async Task R2_CancelledReservation_DoesNotBlockTheRoom()
    {
        var errors = await ValidateAsync(
            async db =>
            {
                var room = db.Rooms.First();
                db.Reservations.Add(new Reservation
                {
                    RoomId = room.Id,
                    OrganizerId = db.Users.First().Id,
                    Title = "Cancelled",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4,
                    Status = ReservationStatus.Cancelled
                });
                await db.SaveChangesAsync();
            },
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Other.Id,
                Title = "Takes the freed slot",
                Start = Now.AddHours(3),
                End = Now.AddHours(5),
                Attendees = 4
            });

        Assert.False(Has(errors, ErrorCodes.RoomDoubleBooked));
    }

    [Fact]
    public async Task R2_DifferentRooms_MayOverlap()
    {
        var errors = await ValidateAsync(
            async db =>
            {
                var rooms = db.Rooms.ToList();
                db.Reservations.Add(new Reservation
                {
                    RoomId = rooms[0].Id,
                    OrganizerId = db.Users.First().Id,
                    Title = "In the small room",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4
                });
                await db.SaveChangesAsync();
            },
            s => new Reservation
            {
                RoomId = s.Large.Id,
                OrganizerId = s.Other.Id,
                Title = "In the large room",
                Start = Now.AddHours(3),
                End = Now.AddHours(5),
                Attendees = 10
            });

        Assert.False(Has(errors, ErrorCodes.RoomDoubleBooked));
    }

    [Fact]
    public async Task R2_UpdatingAReservation_DoesNotClashWithItself()
    {
        int reservationId = 0;
        var errors = await ValidateAsync(
            async db =>
            {
                var room = db.Rooms.First();
                var reservation = new Reservation
                {
                    RoomId = room.Id,
                    OrganizerId = db.Users.First().Id,
                    Title = "Same meeting",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4
                };
                db.Reservations.Add(reservation);
                await db.SaveChangesAsync();
                reservationId = reservation.Id;
            },
            s => new Reservation
            {
                Id = reservationId,
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Same meeting, new time",
                Start = Now.AddHours(2),
                End = Now.AddHours(5),
                Attendees = 4
            });

        Assert.False(Has(errors, ErrorCodes.RoomDoubleBooked));
    }

    // ---------------------------------------------------------------------
    // R3 - no overlapping organizer bookings, even in different rooms.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R3_OrganizerOverlap_InDifferentRooms_IsRejected()
    {
        var errors = await ValidateAsync(
            async db =>
            {
                var rooms = db.Rooms.ToList();
                var organizer = db.Users.First();
                db.Reservations.Add(new Reservation
                {
                    RoomId = rooms[0].Id,
                    OrganizerId = organizer.Id,
                    Title = "First meeting",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4
                });
                await db.SaveChangesAsync();
            },
            s => new Reservation
            {
                RoomId = s.Large.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Overlapping elsewhere",
                Start = Now.AddHours(3),
                End = Now.AddHours(5),
                Attendees = 10
            });

        Assert.True(Has(errors, ErrorCodes.OrganizerDoubleBooked));
    }

    [Fact]
    public async Task R3_DifferentOrganizers_MayOverlap()
    {
        var errors = await ValidateAsync(
            async db =>
            {
                var room = db.Rooms.First();
                db.Reservations.Add(new Reservation
                {
                    RoomId = room.Id,
                    OrganizerId = db.Users.First().Id,
                    Title = "One person",
                    Start = Now.AddHours(2),
                    End = Now.AddHours(4),
                    Attendees = 4
                });
                await db.SaveChangesAsync();
            },
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Other.Id,
                Title = "Another person",
                Start = Now.AddHours(3),
                End = Now.AddHours(5),
                Attendees = 4
            });

        Assert.False(Has(errors, ErrorCodes.OrganizerDoubleBooked));
    }

    // ---------------------------------------------------------------------
    // R4 - start must be in the future and the range must be ordered.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R4_StartInThePast_IsRejected()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Yesterday",
                Start = Now.AddHours(-2),
                End = Now.AddHours(1),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.InvalidTimeRange));
    }

    [Fact]
    public async Task R4_StartNow_IsRejected_BecauseItMustBeInTheFuture()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Starting right now",
                Start = Now,
                End = Now.AddHours(1),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.InvalidTimeRange));
    }

    [Fact]
    public async Task R4_EndBeforeStart_IsRejected()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Backwards",
                Start = Now.AddHours(3),
                End = Now.AddHours(2),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.InvalidTimeRange));
    }

    // ---------------------------------------------------------------------
    // R5 - capacity.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R5_MoreAttendeesThanCapacity_IsRejected()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Too many people",
                Start = Now.AddHours(2),
                End = Now.AddHours(3),
                Attendees = s.Room.Capacity + 1
            });

        Assert.True(Has(errors, ErrorCodes.CapacityExceeded));
    }

    [Fact]
    public async Task R5_AttendeesEqualToCapacity_IsAllowed()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Exactly full",
                Start = Now.AddHours(2),
                End = Now.AddHours(3),
                Attendees = s.Room.Capacity
            });

        Assert.False(Has(errors, ErrorCodes.CapacityExceeded));
    }

    // ---------------------------------------------------------------------
    // R6 - booking horizon.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R6_StartExactlyOnTheHorizon_IsAllowed()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "On the last allowed day",
                Start = Now.AddDays(90),
                End = Now.AddDays(90).AddHours(1),
                Attendees = 4
            });

        Assert.False(Has(errors, ErrorCodes.BookingHorizonExceeded));
    }

    [Fact]
    public async Task R6_StartBeyondTheHorizon_IsRejected()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Too far away",
                Start = Now.AddDays(91),
                End = Now.AddDays(91).AddHours(1),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.BookingHorizonExceeded));
    }

    [Fact]
    public async Task R6_BookingHorizon_FollowsConfiguration()
    {
        var settings = TestContextFactory.DefaultSettings();
        settings.BookingHorizonDays = 30;

        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Forty days out",
                Start = Now.AddDays(40),
                End = Now.AddDays(40).AddHours(1),
                Attendees = 4
            },
            settings);

        Assert.True(Has(errors, ErrorCodes.BookingHorizonExceeded));
    }

    // ---------------------------------------------------------------------
    // R7 - inactive rooms.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task R7_InactiveRoom_IsRejected()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Inactive.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Renovation room",
                Start = Now.AddHours(2),
                End = Now.AddHours(3),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.RoomInactive));
    }

    [Fact]
    public async Task R7_ActiveRoom_IsNotFlagged()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Normal room",
                Start = Now.AddHours(2),
                End = Now.AddHours(3),
                Attendees = 4
            });

        Assert.False(Has(errors, ErrorCodes.RoomInactive));
    }

    // ---------------------------------------------------------------------
    // Cross-cutting behaviour.
    // ---------------------------------------------------------------------
    [Fact]
    public async Task MultipleBrokenRules_AreAllReported()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Inactive.Id,           // R7 inactive
                OrganizerId = s.Organizer.Id,
                Title = "Everything wrong",
                Start = Now.AddDays(91),          // R6 horizon
                End = Now.AddDays(99),            // R1 duration
                Attendees = 500                   // R5 capacity
            });

        Assert.True(Has(errors, ErrorCodes.RoomInactive));
        Assert.True(Has(errors, ErrorCodes.BookingHorizonExceeded));
        Assert.True(Has(errors, ErrorCodes.MaxDurationExceeded));
        Assert.True(Has(errors, ErrorCodes.CapacityExceeded));
    }

    [Fact]
    public async Task UnknownRoom_IsReportedAsNotFound()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = 9999,
                OrganizerId = s.Organizer.Id,
                Title = "Ghost room",
                Start = Now.AddHours(2),
                End = Now.AddHours(3),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.RoomNotFound));
    }

    [Fact]
    public async Task UnknownOrganizer_IsReportedAsNotFound()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = 9999,
                Title = "Ghost organizer",
                Start = Now.AddHours(2),
                End = Now.AddHours(3),
                Attendees = 4
            });

        Assert.True(Has(errors, ErrorCodes.UserNotFound));
    }

    [Fact]
    public async Task ValidReservation_ProducesNoErrors()
    {
        var errors = await ValidateAsync(
            null,
            s => new Reservation
            {
                RoomId = s.Room.Id,
                OrganizerId = s.Organizer.Id,
                Title = "Perfectly fine",
                Start = Now.AddDays(1),
                End = Now.AddDays(1).AddHours(2),
                Attendees = 4
            });

        Assert.Empty(errors);
    }
}
