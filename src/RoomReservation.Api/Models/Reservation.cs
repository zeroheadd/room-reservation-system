namespace RoomReservation.Api.Models;

/// <summary>
/// Lifecycle of a reservation: Booked -> Completed, or Booked -> Cancelled.
/// Cancelled reservations never block a room or an organizer (rules R2/R3).
/// </summary>
public enum ReservationStatus
{
    Booked = 1,
    Completed = 2,
    Cancelled = 3
}

/// <summary>
/// A meeting booked in a room by an organizer.
/// </summary>
public class Reservation
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public Room? Room { get; set; }

    /// <summary>The organizer (owner) of the meeting. A user cannot double-book themselves (rule R3).</summary>
    public int OrganizerId { get; set; }

    public User? Organizer { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    /// <summary>Number of people expected. Must fit the room capacity (rule R5).</summary>
    public int Attendees { get; set; }

    public ReservationStatus Status { get; set; } = ReservationStatus.Booked;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Set when the meeting is cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Set when the meeting is closed - either because its end time passed or because it was
    /// ended early. This is the "actually used" timestamp behind the room usage report.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// How many people actually showed up. Captured when the organizer ends the meeting,
    /// which is the signal the room usage report uses to find over-booked, under-used rooms.
    /// </summary>
    public int? ActualAttendees { get; set; }

    public TimeSpan Duration => End - Start;
}
