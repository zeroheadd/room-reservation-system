using RoomReservation.Api.Models;

namespace RoomReservation.Api.Dtos;

/// <summary>Room as returned by the API, including usage counters for the rooms page.</summary>
public record RoomDto(
    int Id,
    string Name,
    string Code,
    int Capacity,
    string Floor,
    IReadOnlyList<string> Equipment,
    bool IsActive,
    int ActiveReservations,
    int TotalReservations);

/// <summary>User as returned by the API, including the number of active reservations.</summary>
public record UserDto(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string Code,
    string Email,
    string Department,
    int ActiveReservations,
    int TotalReservations);

/// <summary>A reservation as returned by the API with its room and organizer expanded.</summary>
public record ReservationDto(
    int Id,
    int RoomId,
    string RoomName,
    string RoomCode,
    int OrganizerId,
    string OrganizerName,
    string Title,
    string? Notes,
    DateTime Start,
    DateTime End,
    double DurationHours,
    int Attendees,
    string Status,
    int? ActualAttendees,
    DateTime? CompletedAt,
    DateTime? CancelledAt,
    DateTime CreatedAt);

/// <summary>Payload for creating a reservation.</summary>
public record CreateReservationRequest(
    int RoomId,
    int OrganizerId,
    string Title,
    DateTime Start,
    DateTime End,
    int Attendees,
    string? Notes);

/// <summary>Payload for updating a reservation. Status is changed through the dedicated actions.</summary>
public record UpdateReservationRequest(
    int RoomId,
    int OrganizerId,
    string Title,
    DateTime Start,
    DateTime End,
    int Attendees,
    string? Notes);

/// <summary>Payload for ending a meeting early.</summary>
public record EndMeetingRequest(int? ActualAttendees);

public static class Mappings
{
    public static string ToApiValue(this Equipment equipment) => equipment switch
    {
        Equipment.Projector => "Projector",
        Equipment.TvScreen => "TV screen",
        Equipment.VideoConference => "Video conferencing",
        Equipment.Whiteboard => "Whiteboard",
        _ => equipment.ToString()
    };

    public static RoomDto ToDto(this Room room) => new(
        room.Id,
        room.Name,
        room.Code,
        room.Capacity,
        room.Floor,
        room.Equipment.Select(e => e.ToApiValue()).ToList(),
        room.IsActive,
        room.Reservations.Count(r => r.Status != ReservationStatus.Cancelled),
        room.Reservations.Count);

    public static UserDto ToDto(this User user) => new(
        user.Id,
        user.FirstName,
        user.LastName,
        user.FullName,
        user.Code,
        user.Email,
        user.Department,
        user.Reservations.Count(r => r.Status != ReservationStatus.Cancelled),
        user.Reservations.Count);

    public static ReservationDto ToDto(this Reservation reservation) => new(
        reservation.Id,
        reservation.RoomId,
        reservation.Room?.Name ?? string.Empty,
        reservation.Room?.Code ?? string.Empty,
        reservation.OrganizerId,
        reservation.Organizer?.FullName ?? string.Empty,
        reservation.Title,
        reservation.Notes,
        reservation.Start,
        reservation.End,
        Math.Round((reservation.End - reservation.Start).TotalHours, 2),
        reservation.Attendees,
        reservation.Status.ToString(),
        reservation.ActualAttendees,
        reservation.CompletedAt,
        reservation.CancelledAt,
        reservation.CreatedAt);
}
