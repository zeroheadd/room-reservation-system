using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Services;

/// <summary>
/// The single place where the booking rules R1-R7 are enforced. Every create and update
/// goes through <see cref="ValidateAsync"/> <b>before</b> anything is persisted, so an
/// invalid reservation can never reach the database.
/// </summary>
public interface IBookingRuleValidator
{
    Task<IReadOnlyList<ValidationError>> ValidateAsync(Reservation candidate, CancellationToken ct = default);
}

public class BookingRuleValidator : IBookingRuleValidator
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;
    private readonly BookingSettings _settings;

    public BookingRuleValidator(AppDbContext db, TimeProvider clock, BookingSettings settings)
    {
        _db = db;
        _clock = clock;
        _settings = settings;
    }

    public async Task<IReadOnlyList<ValidationError>> ValidateAsync(Reservation candidate, CancellationToken ct = default)
    {
        var errors = new List<ValidationError>();

        // R4 - start must be in the future and the range must be well formed. Everything else
        // assumes a sane range, so it is checked first.
        ValidateTimeRange(candidate, errors);

        // R1 - maximum duration.
        ValidateDuration(candidate, errors);

        // R6 - booking horizon.
        ValidateHorizon(candidate, errors);

        var room = await _db.Rooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == candidate.RoomId, ct);

        if (room is null)
        {
            errors.Add(new ValidationError(ErrorCodes.RoomNotFound, $"Room {candidate.RoomId} does not exist."));
            return errors;
        }

        // R7 - only active rooms can be booked.
        if (!room.IsActive)
        {
            errors.Add(new ValidationError(
                ErrorCodes.RoomInactive,
                $"Room '{room.Name}' is inactive and cannot be booked."));
        }

        // R5 - attendees must fit the room.
        if (candidate.Attendees > room.Capacity)
        {
            errors.Add(new ValidationError(
                ErrorCodes.CapacityExceeded,
                $"Room '{room.Name}' seats {room.Capacity} people but the meeting has {candidate.Attendees} attendees."));
        }

        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == candidate.OrganizerId, ct))
        {
            errors.Add(new ValidationError(ErrorCodes.UserNotFound, $"User {candidate.OrganizerId} does not exist."));
        }

        // R2 / R3 - overlaps, only when the range itself is valid.
        if (IsRangeOrdered(candidate))
        {
            await ValidateRoomOverlapAsync(candidate, room, errors, ct);
            await ValidateOrganizerOverlapAsync(candidate, errors, ct);
        }

        return errors;
    }

    private void ValidateTimeRange(Reservation candidate, List<ValidationError> errors)
    {
        if (candidate.End <= candidate.Start)
        {
            errors.Add(new ValidationError(
                ErrorCodes.InvalidTimeRange,
                "The end time must be after the start time."));
        }

        // R4 - start must be in the future.
        if (candidate.Start <= _clock.GetUtcNow().UtcDateTime)
        {
            errors.Add(new ValidationError(
                ErrorCodes.InvalidTimeRange,
                "The meeting must start in the future."));
        }
    }

    private void ValidateDuration(Reservation candidate, List<ValidationError> errors)
    {
        if (!IsRangeOrdered(candidate))
        {
            return;
        }

        // R1 - exactly MaxReservationDurationDays is allowed, longer is rejected.
        var max = TimeSpan.FromDays(_settings.MaxReservationDurationDays);
        if (candidate.End - candidate.Start > max)
        {
            errors.Add(new ValidationError(
                ErrorCodes.MaxDurationExceeded,
                $"A reservation cannot be longer than {_settings.MaxReservationDurationDays} days."));
        }
    }

    private void ValidateHorizon(Reservation candidate, List<ValidationError> errors)
    {
        // R6 - the start may not be further away than the configured horizon.
        var latestStart = _clock.GetUtcNow().UtcDateTime.AddDays(_settings.BookingHorizonDays);
        if (candidate.Start > latestStart)
        {
            errors.Add(new ValidationError(
                ErrorCodes.BookingHorizonExceeded,
                $"A reservation cannot start more than {_settings.BookingHorizonDays} days from now."));
        }
    }

    private async Task ValidateRoomOverlapAsync(
        Reservation candidate,
        Room room,
        List<ValidationError> errors,
        CancellationToken ct)
    {
        // R2 - a room cannot hold two overlapping active reservations. Cancelled ones do not block.
        var clash = await ActiveReservations()
            .Where(r => r.RoomId == candidate.RoomId)
            .Where(r => r.Id != candidate.Id)
            .Where(r => r.Start < candidate.End && candidate.Start < r.End)
            .OrderBy(r => r.Start)
            .Select(r => new { r.Id, r.Start, r.End, r.Title })
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
        {
            errors.Add(new ValidationError(
                ErrorCodes.RoomDoubleBooked,
                $"Room '{room.Name}' is already booked from {clash.Start:u} to {clash.End:u} (reservation #{clash.Id} '{clash.Title}')."));
        }
    }

    private async Task ValidateOrganizerOverlapAsync(
        Reservation candidate,
        List<ValidationError> errors,
        CancellationToken ct)
    {
        // R3 - a user cannot organize two overlapping meetings, even in different rooms.
        var clash = await ActiveReservations()
            .Where(r => r.OrganizerId == candidate.OrganizerId)
            .Where(r => r.Id != candidate.Id)
            .Where(r => r.Start < candidate.End && candidate.Start < r.End)
            .OrderBy(r => r.Start)
            .Select(r => new { r.Id, r.Start, r.End, r.Title })
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
        {
            errors.Add(new ValidationError(
                ErrorCodes.OrganizerDoubleBooked,
                $"The organizer already has a meeting from {clash.Start:u} to {clash.End:u} (reservation #{clash.Id} '{clash.Title}')."));
        }
    }

    /// <summary>Only Booked and Completed reservations occupy a room; cancelled ones are ignored.</summary>
    private IQueryable<Reservation> ActiveReservations() =>
        _db.Reservations
            .AsNoTracking()
            .Where(r => r.Status != ReservationStatus.Cancelled);

    private static bool IsRangeOrdered(Reservation candidate) => candidate.End > candidate.Start;
}
