namespace RoomReservation.Api.Models;

/// <summary>
/// Equipment that can be installed in a meeting room. A room may have several items.
/// </summary>
public enum Equipment
{
    Projector = 1,
    TvScreen = 2,
    VideoConference = 3,
    Whiteboard = 4
}

/// <summary>
/// A bookable meeting room.
/// </summary>
public class Room
{
    public int Id { get; set; }

    /// <summary>Human readable room name, e.g. "Aurora".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unique business identifier shown to users, e.g. "KA-201".</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Maximum number of people the room can host (rule R5).</summary>
    public int Capacity { get; set; }

    /// <summary>Floor / location, e.g. "2nd floor, west wing".</summary>
    public string Floor { get; set; } = string.Empty;

    /// <summary>Equipment installed in the room.</summary>
    public List<Equipment> Equipment { get; set; } = new();

    /// <summary>Inactive rooms cannot be booked (rule R7).</summary>
    public bool IsActive { get; set; } = true;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
