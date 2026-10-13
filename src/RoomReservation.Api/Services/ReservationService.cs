using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Services;

/// <summary>
/// All reservation write operations. Every operation runs the booking rule validator before
/// anything is saved, so no rule-breaking reservation can ever be persisted.
/// </summary>
public interface IReservationService
{
    Task<ReservationResult> CreateAsync(CreateReservationRequest request, CancellationToken ct = default);

    Task<ReservationResult> UpdateAsync(int id, UpdateReservationRequest request, CancellationToken ct = default);

    Task<ReservationResult> CancelAsync(int id, CancellationToken ct = default);

    Task<ReservationResult> EndEarlyAsync(int id, EndMeetingRequest request, CancellationToken ct = default);
}

public class ReservationService : IReservationService
{
    private readonly AppDbContext _db;
    private readonly IBookingRuleValidator _validator;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        AppDbContext db,
        IBookingRuleValidator validator,
        TimeProvider clock,
        ILogger<ReservationService> logger)
    {
        _db = db;
        _validator = validator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ReservationResult> CreateAsync(CreateReservationRequest request, CancellationToken ct = default)
    {
        var candidate = new Reservation
        {
            RoomId = request.RoomId,
            OrganizerId = request.OrganizerId,
            Title = request.Title?.Trim() ?? string.Empty,
            Notes = request.Notes,
            Start = request.Start,
            End = request.End,
            Attendees = request.Attendees,
            Status = ReservationStatus.Booked,
            CreatedAt = _clock.GetUtcNow().UtcDateTime
        };

        var errors = await _validator.ValidateAsync(candidate, ct);
        if (errors.Count > 0)
        {
            _logger.LogInformation("Rejected new reservation: {Codes}", string.Join(", ", errors.Select(e => e.Code)));
            return ReservationResult.RuleViolation(errors);
        }

        _db.Reservations.Add(candidate);
        await _db.SaveChangesAsync(ct);

        var created = await LoadAsync(candidate.Id, ct);
        return ReservationResult.Created(created!.ToDto());
    }

    public async Task<ReservationResult> UpdateAsync(int id, UpdateReservationRequest request, CancellationToken ct = default)
    {
        var reservation = await _db.Reservations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null)
        {
            return ReservationResult.NotFound(ErrorCodes.ReservationNotFound, $"Reservation {id} does not exist.");
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return ReservationResult.Conflict(
                ErrorCodes.ReservationNotModifiable,
                "A cancelled reservation cannot be edited. Create a new reservation instead.");
        }

        // Validate the candidate state before mutating the tracked entity.
        var candidate = new Reservation
        {
            Id = reservation.Id,
            RoomId = request.RoomId,
            OrganizerId = request.OrganizerId,
            Title = request.Title?.Trim() ?? string.Empty,
            Notes = request.Notes,
            Start = request.Start,
            End = request.End,
            Attendees = request.Attendees,
            Status = reservation.Status,
            CreatedAt = reservation.CreatedAt
        };

        var errors = await _validator.ValidateAsync(candidate, ct);
        if (errors.Count > 0)
        {
            _logger.LogInformation("Rejected update of reservation {Id}: {Codes}", id, string.Join(", ", errors.Select(e => e.Code)));
            return ReservationResult.RuleViolation(errors);
        }

        reservation.RoomId = candidate.RoomId;
        reservation.OrganizerId = candidate.OrganizerId;
        reservation.Title = candidate.Title;
        reservation.Notes = candidate.Notes;
        reservation.Start = candidate.Start;
        reservation.End = candidate.End;
        reservation.Attendees = candidate.Attendees;

        await _db.SaveChangesAsync(ct);

        var updated = await LoadAsync(reservation.Id, ct);
        return ReservationResult.Ok(updated!.ToDto());
    }

    public async Task<ReservationResult> CancelAsync(int id, CancellationToken ct = default)
    {
        var reservation = await _db.Reservations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null)
        {
            return ReservationResult.NotFound(ErrorCodes.ReservationNotFound, $"Reservation {id} does not exist.");
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return ReservationResult.Conflict(ErrorCodes.ReservationNotCancellable, "This reservation is already cancelled.");
        }

        if (reservation.Status == ReservationStatus.Completed)
        {
            return ReservationResult.Conflict(ErrorCodes.ReservationNotCancellable, "A finished meeting cannot be cancelled.");
        }

        reservation.Status = ReservationStatus.Cancelled;
        reservation.CancelledAt = _clock.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(ct);

        var cancelled = await LoadAsync(reservation.Id, ct);
        return ReservationResult.Ok(cancelled!.ToDto());
    }

    public async Task<ReservationResult> EndEarlyAsync(int id, EndMeetingRequest request, CancellationToken ct = default)
    {
        var reservation = await _db.Reservations.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reservation is null)
        {
            return ReservationResult.NotFound(ErrorCodes.ReservationNotFound, $"Reservation {id} does not exist.");
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return ReservationResult.Conflict(ErrorCodes.ReservationNotEndable, "A cancelled reservation cannot be ended.");
        }

        if (reservation.Status == ReservationStatus.Completed)
        {
            return ReservationResult.Conflict(ErrorCodes.ReservationNotEndable, "This meeting was already closed.");
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        if (now < reservation.Start)
        {
            return ReservationResult.Conflict(
                ErrorCodes.ReservationNotStarted,
                "The meeting has not started yet. Cancel it instead if the slot is no longer needed.");
        }

        if (request.ActualAttendees is < 0)
        {
            return ReservationResult.RuleViolation(new[]
            {
                new ValidationError(ErrorCodes.ValidationFailed, "Actual attendees cannot be negative.")
            });
        }

        if (request.ActualAttendees > reservation.Attendees)
        {
            return ReservationResult.RuleViolation(new[]
            {
                new ValidationError(
                    ErrorCodes.ValidationFailed,
                    $"Actual attendees ({request.ActualAttendees}) cannot exceed the booked number ({reservation.Attendees}).")
            });
        }

        reservation.Status = ReservationStatus.Completed;
        reservation.CompletedAt = now;
        if (request.ActualAttendees is not null)
        {
            reservation.ActualAttendees = request.ActualAttendees;
        }

        await _db.SaveChangesAsync(ct);

        var ended = await LoadAsync(reservation.Id, ct);
        return ReservationResult.Ok(ended!.ToDto());
    }

    private Task<Reservation?> LoadAsync(int id, CancellationToken ct) =>
        _db.Reservations
            .AsNoTracking()
            .Include(r => r.Room)
            .Include(r => r.Organizer)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
}
