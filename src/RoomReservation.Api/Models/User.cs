namespace RoomReservation.Api.Models;

/// <summary>
/// A person who can organize meetings. There is no authentication in this app.
/// </summary>
public class User
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>Unique business identifier shown to users, e.g. "EMP-0042".</summary>
    public string Code { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}";

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
