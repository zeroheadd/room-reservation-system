using RoomReservation.Api.Data;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;
using Xunit;

namespace RoomReservation.Api.Tests;

/// <summary>
/// The room usage report is what tells the office which rooms are booked but not used,
/// so its numbers are pinned down by tests.
/// </summary>
public class RoomUsageReportServiceTests
{
    private static readonly DateTime Now = TestContextFactory.Now;

    private static (RoomUsageReportService Service, AppDbContext Db) Create(BookingSettings? settings = null)
    {
        var db = TestContextFactory.CreateContext();
        var effective = settings ?? TestContextFactory.DefaultSettings();
        var service = new RoomUsageReportService(
            db,
            new FixedTimeProvider(Now),
            effective,
            new RoomUsageReportRenderer());

        return (service, db);
    }

    private static Reservation Finished(
        int roomId,
        int organizerId,
        int dayOffset,
        int attendees,
        int actualAttendees,
        double durationHours = 2)
    {
        var start = Now.Date.AddDays(dayOffset).AddHours(10);
        var end = start.AddHours(durationHours);
        return new Reservation
        {
            RoomId = roomId,
            OrganizerId = organizerId,
            Title = $"Meeting d{dayOffset}",
            Start = start,
            End = end,
            Attendees = attendees,
            ActualAttendees = actualAttendees,
            Status = ReservationStatus.Completed,
            CompletedAt = end
        };
    }

    [Fact]
    public async Task Report_FlagsARoomThatIsBookedOftenButRarelyAttended()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);

        // Four finished meetings in the small room, only 2 of 20 booked people turned up.
        db.Reservations.AddRange(
            Finished(seed.Small.Id, seed.Organizer.Id, -4, 5, 1),
            Finished(seed.Small.Id, seed.Organizer.Id, -3, 5, 0),
            Finished(seed.Small.Id, seed.Organizer.Id, -2, 5, 1),
            Finished(seed.Small.Id, seed.Organizer.Id, -1, 5, 0));

        // Three well attended meetings in the large room. Three is the configured minimum, so
        // the room gets a real verdict rather than "not enough data".
        db.Reservations.AddRange(
            Finished(seed.Large.Id, seed.Other.Id, -4, 20, 19),
            Finished(seed.Large.Id, seed.Other.Id, -3, 20, 19),
            Finished(seed.Large.Id, seed.Other.Id, -2, 20, 18));
        await db.SaveChangesAsync();

        var report = await service.BuildAsync(null);

        var small = report.Rooms.Single(r => r.RoomId == seed.Small.Id);
        Assert.True(small.IsUnderused);
        Assert.Equal(UsageVerdicts.Underused, small.Verdict);
        Assert.Equal(20, small.BookedAttendees);
        Assert.Equal(2, small.ActualAttendees);
        Assert.Equal(0.1, small.Utilization!.Value, 3);

        var large = report.Rooms.Single(r => r.RoomId == seed.Large.Id);
        Assert.False(large.IsUnderused);
        Assert.Equal(UsageVerdicts.WellUsed, large.Verdict);

        // Worst utilization first: the underused room leads the report.
        Assert.Equal(seed.Small.Id, report.Rooms[0].RoomId);
        Assert.Equal(1, report.Summary.UnderusedRooms);
    }

    [Fact]
    public async Task Report_DoesNotLabelARoomWithoutEnoughFinishedMeetings()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);

        // Only two finished meetings, below MinimumMeetingsForUsageVerdict (3).
        db.Reservations.AddRange(
            Finished(seed.Small.Id, seed.Organizer.Id, -2, 5, 0),
            Finished(seed.Small.Id, seed.Organizer.Id, -1, 5, 0));
        await db.SaveChangesAsync();

        var report = await service.BuildAsync(null);
        var small = report.Rooms.Single(r => r.RoomId == seed.Small.Id);

        Assert.False(small.IsUnderused);
        Assert.Equal(UsageVerdicts.NotEnoughData, small.Verdict);
        Assert.Equal(0.0, small.Utilization!.Value, 3);
    }

    [Fact]
    public async Task Report_IgnoresCancelledReservations()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);

        db.Reservations.Add(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Cancelled",
            Start = Now.AddDays(-3),
            End = Now.AddDays(-3).AddHours(2),
            Attendees = 6,
            Status = ReservationStatus.Cancelled,
            CancelledAt = Now.AddDays(-4)
        });
        db.Reservations.AddRange(
            Finished(seed.Small.Id, seed.Organizer.Id, -2, 5, 5),
            Finished(seed.Small.Id, seed.Organizer.Id, -1, 5, 5));
        await db.SaveChangesAsync();

        var report = await service.BuildAsync(null);
        var small = report.Rooms.Single(r => r.RoomId == seed.Small.Id);

        Assert.Equal(1, small.CancelledReservations);
        Assert.Equal(2, small.ActiveReservations);
        Assert.Equal(2, small.CompletedMeetings);
        Assert.Equal(1.0, small.Utilization!.Value, 3);
        Assert.False(small.IsUnderused);
    }

    [Fact]
    public async Task Report_CountsEarlyEndedMeetingsAsShorterUsage()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);

        // A three hour slot that was ended after thirty minutes.
        var start = Now.AddHours(-3);
        db.Reservations.Add(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Very short",
            Start = start,
            End = start.AddHours(3),
            Attendees = 5,
            ActualAttendees = 1,
            Status = ReservationStatus.Completed,
            CompletedAt = start.AddMinutes(30)
        });
        await db.SaveChangesAsync();

        var report = await service.BuildAsync(null);
        var small = report.Rooms.Single(r => r.RoomId == seed.Small.Id);

        Assert.Equal(3, small.ReservedHours);
        Assert.Equal(0.5, small.ActualHours);
        Assert.Equal(2.5, small.WastedHours);
    }

    [Fact]
    public async Task Report_LastDaysFilter_ExcludesOlderReservations()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);

        db.Reservations.AddRange(
            Finished(seed.Small.Id, seed.Organizer.Id, -40, 5, 5),
            Finished(seed.Small.Id, seed.Organizer.Id, -5, 5, 5),
            Finished(seed.Small.Id, seed.Organizer.Id, -1, 5, 5));
        await db.SaveChangesAsync();

        var all = await service.BuildAsync(null);
        var recent = await service.BuildAsync(10);

        Assert.Equal(3, all.Rooms.Single(r => r.RoomId == seed.Small.Id).TotalReservations);
        Assert.Equal(2, recent.Rooms.Single(r => r.RoomId == seed.Small.Id).TotalReservations);
        Assert.Equal(10, recent.LastDays);
        Assert.Null(all.LastDays);
    }

    [Fact]
    public async Task Report_Summary_AddsUpAcrossRooms()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);

        db.Reservations.AddRange(
            Finished(seed.Small.Id, seed.Organizer.Id, -2, 5, 5, 2),
            Finished(seed.Large.Id, seed.Other.Id, -1, 10, 5, 2));
        await db.SaveChangesAsync();

        var report = await service.BuildAsync(null);

        Assert.Equal(3, report.Summary.Rooms);
        Assert.Equal(2, report.Summary.TotalReservations);
        Assert.Equal(4, report.Summary.ReservedHours);
        Assert.Equal(4, report.Summary.ActualHours);
        Assert.Equal(0.667, report.Summary.Utilization, 3);
    }

    [Fact]
    public async Task Report_UsesTheConfiguredThreshold()
    {
        var settings = TestContextFactory.DefaultSettings();
        settings.UnderusedThreshold = 0.95;
        settings.MinimumMeetingsForUsageVerdict = 1;

        var (service, db) = Create(settings);
        var seed = TestContextFactory.Seed(db);

        // 80% attendance is fine by default but not with a 95% threshold.
        db.Reservations.Add(Finished(seed.Small.Id, seed.Organizer.Id, -1, 10, 8));
        await db.SaveChangesAsync();

        var report = await service.BuildAsync(null);
        var small = report.Rooms.Single(r => r.RoomId == seed.Small.Id);

        Assert.True(small.IsUnderused);
        Assert.Equal(0.95, report.UnderusedThreshold);
    }

    [Fact]
    public async Task PrintableHtml_IsSelfContainedAndTriggersPrinting()
    {
        var (service, db) = Create();
        var seed = TestContextFactory.Seed(db);
        db.Reservations.Add(Finished(seed.Small.Id, seed.Organizer.Id, -1, 5, 1));
        await db.SaveChangesAsync();

        var html = await service.RenderHtmlAsync(null);

        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Room usage report", html);
        Assert.Contains("window.print()", html);
        Assert.Contains(seed.Small.Name, html);
        // No external assets, so it works offline and inside the browser print dialog.
        Assert.DoesNotContain("<link", html);
        Assert.DoesNotContain("<script src", html);
    }
}
