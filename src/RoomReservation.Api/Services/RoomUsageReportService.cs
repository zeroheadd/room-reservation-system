using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Services;

/// <summary>
/// Builds the room usage report: which rooms are booked a lot but actually used very little.
/// It compares what was booked (hours, attendees) with what really happened, using the
/// attendance captured when a meeting is closed.
/// </summary>
public interface IRoomUsageReportService
{
    Task<RoomUsageReportDto> BuildAsync(int? lastDays, CancellationToken ct = default);

    /// <summary>Renders the same report as a printable HTML document (used for the PDF export).</summary>
    Task<string> RenderHtmlAsync(int? lastDays, CancellationToken ct = default);
}

/// <summary>Turns a report into a self-contained, print-ready HTML document.</summary>
public interface IRoomUsageReportRenderer
{
    string Render(RoomUsageReportDto report);
}

/// <summary>The verdict labels the room usage report can produce.</summary>
public static class UsageVerdicts
{
    public const string Underused = "Often booked, rarely used";
    public const string NotEnoughData = "Not enough data";
    public const string Moderate = "Moderately used";
    public const string WellUsed = "Well used";
}

public class RoomUsageReportService : IRoomUsageReportService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly BookingSettings _settings;
    private readonly IRoomUsageReportRenderer _renderer;

    public RoomUsageReportService(
        AppDbContext db,
        TimeProvider clock,
        BookingSettings settings,
        IRoomUsageReportRenderer renderer)
    {
        _db = db;
        _clock = clock;
        _settings = settings;
        _renderer = renderer;
    }

    public async Task<RoomUsageReportDto> BuildAsync(int? lastDays, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var from = lastDays is > 0 ? now.AddDays(-lastDays.Value) : (DateTime?)null;

        var rooms = await _db.Rooms
            .AsNoTracking()
            .Include(r => r.Reservations)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var rows = rooms.Select(room => BuildRoomUsage(room, now, from)).ToList();

        var summary = new RoomUsageSummaryDto(
            Rooms: rows.Count,
            TotalReservations: rows.Sum(r => r.TotalReservations),
            CompletedMeetings: rows.Sum(r => r.CompletedMeetings),
            CancelledReservations: rows.Sum(r => r.CancelledReservations),
            ReservedHours: Math.Round(rows.Sum(r => r.ReservedHours), 2),
            ActualHours: Math.Round(rows.Sum(r => r.ActualHours), 2),
            WastedHours: Math.Round(rows.Sum(r => r.WastedHours), 2),
            Utilization: Ratio(rows.Sum(r => r.ActualAttendees), rows.Sum(r => r.BookedAttendees)) ?? 0,
            UnderusedRooms: rows.Count(r => r.IsUnderused));

        // Worst offenders first: that is the list the office actually wants to act on.
        var sorted = rows
            .OrderBy(r => r.Utilization ?? double.MaxValue)
            .ThenByDescending(r => r.WastedHours)
            .ToList();

        return new RoomUsageReportDto(
            GeneratedAt: now,
            LastDays: lastDays is > 0 ? lastDays : null,
            UnderusedThreshold: _settings.UnderusedThreshold,
            MinimumMeetingsForUsageVerdict: _settings.MinimumMeetingsForUsageVerdict,
            Summary: summary,
            Rooms: sorted);
    }

    private RoomUsageDto BuildRoomUsage(Room room, DateTime now, DateTime? from)
    {
        var reservations = room.Reservations
            .Where(r => from is null || r.Start >= from)
            .ToList();

        var cancelled = reservations.Where(r => r.Status == ReservationStatus.Cancelled).ToList();
        var live = reservations.Where(r => r.Status != ReservationStatus.Cancelled).ToList();

        // Meetings that are already over (status Completed, or ended in the past).
        var closed = live.Where(IsClosed).ToList();

        var reservedHours = live.Sum(r => (r.End - r.Start).TotalHours);
        var actualHours = closed.Sum(ActualHours);
        var wastedHours = Math.Max(0, reservedHours - actualHours);

        // Attendance is only known for meetings where the organizer recorded who showed up.
        var withAttendance = closed.Where(r => r.ActualAttendees.HasValue).ToList();
        var bookedAttendees = withAttendance.Sum(r => r.Attendees);
        var actualAttendees = withAttendance.Sum(r => r.ActualAttendees!.Value);
        var utilization = Ratio(actualAttendees, bookedAttendees);

        var isUnderused =
            withAttendance.Count >= _settings.MinimumMeetingsForUsageVerdict &&
            utilization is not null &&
            utilization < _settings.UnderusedThreshold;

        return new RoomUsageDto(
            RoomId: room.Id,
            RoomName: room.Name,
            RoomCode: room.Code,
            Floor: room.Floor,
            Capacity: room.Capacity,
            IsActive: room.IsActive,
            TotalReservations: reservations.Count,
            ActiveReservations: live.Count,
            CompletedMeetings: closed.Count,
            CancelledReservations: cancelled.Count,
            ReservedHours: Math.Round(reservedHours, 2),
            ActualHours: Math.Round(actualHours, 2),
            WastedHours: Math.Round(wastedHours, 2),
            BookedAttendees: bookedAttendees,
            ActualAttendees: actualAttendees,
            MeetingsWithAttendance: withAttendance.Count,
            Utilization: utilization,
            IsUnderused: isUnderused,
            Verdict: Verdict(isUnderused, withAttendance.Count, utilization));

        bool IsClosed(Reservation r) =>
            r.Status == ReservationStatus.Completed || r.End <= now;

        double ActualHours(Reservation r)
        {
            // If the organizer ended the meeting early, only the real time counts as used.
            var end = r.CompletedAt is not null && r.CompletedAt < r.End ? r.CompletedAt.Value : r.End;
            var hours = (end - r.Start).TotalHours;
            return hours > 0 ? hours : 0;
        }
    }

    private string Verdict(bool isUnderused, int meetingsWithAttendance, double? utilization)
    {
        if (isUnderused)
        {
            return UsageVerdicts.Underused;
        }

        if (utilization is null || meetingsWithAttendance < _settings.MinimumMeetingsForUsageVerdict)
        {
            return UsageVerdicts.NotEnoughData;
        }

        return utilization < 0.8 ? UsageVerdicts.Moderate : UsageVerdicts.WellUsed;
    }

    private static double? Ratio(int actual, int booked) =>
        booked <= 0 ? null : Math.Round((double)actual / booked, 3);

    public async Task<string> RenderHtmlAsync(int? lastDays, CancellationToken ct = default)
    {
        var report = await BuildAsync(lastDays, ct);
        return _renderer.Render(report);
    }
}
