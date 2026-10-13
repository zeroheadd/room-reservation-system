using System.ComponentModel.DataAnnotations;

namespace RoomReservation.Api.Models;

/// <summary>
/// Business limits for booking rules. Bound from the "BookingSettings" section of
/// appsettings.json so that the numeric limits are configurable and never hard-coded.
/// </summary>
public class BookingSettings
{
    public const string SectionName = "BookingSettings";

    /// <summary>Rule R1 - a single reservation may not last longer than this many days. Exactly this value is allowed.</summary>
    [Range(1, 365)]
    public int MaxReservationDurationDays { get; set; } = 7;

    /// <summary>Rule R6 - a reservation may not start more than this many days in the future.</summary>
    [Range(1, 3650)]
    public int BookingHorizonDays { get; set; } = 90;

    /// <summary>
    /// The room usage report flags a room as "often booked but rarely used" when its
    /// utilization ratio (actual attendance / booked attendance) falls below this value.
    /// </summary>
    [Range(0.0, 1.0)]
    public double UnderusedThreshold { get; set; } = 0.5;

    /// <summary>A room needs at least this many finished meetings before the report labels it.</summary>
    [Range(0, 1000)]
    public int MinimumMeetingsForUsageVerdict { get; set; } = 3;
}
