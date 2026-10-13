using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;

namespace RoomReservation.Api.Controllers;

/// <summary>
/// Reservations page: list, filter, create, edit, cancel and end-early.
/// Every write is validated against booking rules R1-R7 by the service layer.
/// </summary>
[ApiController]
[Route("api/reservations")]
[Produces("application/json")]
public class ReservationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IReservationService _reservations;

    public ReservationsController(AppDbContext db, IReservationService reservations)
    {
        _db = db;
        _reservations = reservations;
    }

    /// <summary>Lists reservations with optional filters.</summary>
    /// <param name="roomId">Only reservations for this room.</param>
    /// <param name="organizerId">Only reservations organized by this user.</param>
    /// <param name="status">Booked, Completed or Cancelled.</param>
    /// <param name="from">Only reservations that end at or after this moment (ISO 8601).</param>
    /// <param name="to">Only reservations that start at or before this moment (ISO 8601).</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ReservationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<ReservationDto>>> GetReservations(
        [FromQuery] int? roomId,
        [FromQuery] int? organizerId,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct = default)
    {
        var query = _db.Reservations
            .AsNoTracking()
            .Include(r => r.Room)
            .Include(r => r.Organizer)
            .AsQueryable();

        if (roomId is not null)
        {
            query = query.Where(r => r.RoomId == roomId);
        }

        if (organizerId is not null)
        {
            query = query.Where(r => r.OrganizerId == organizerId);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!ReservationStatuses.TryParse(status, out var parsedStatus))
            {
                return QueryFilters.BadRequest(
                    ErrorCodes.ValidationFailed,
                    $"Unknown status '{status}'. Allowed values: Booked, Completed, Cancelled.");
            }

            query = query.Where(r => r.Status == parsedStatus);
        }

        // Date range filter: keep reservations intersecting [from, to].
        if (QueryFilters.TryParseDate(from, nameof(from), out var fromDate, out var fromError))
        {
            query = query.Where(r => r.End >= fromDate);
        }
        else if (fromError is not null)
        {
            return fromError;
        }

        if (QueryFilters.TryParseDate(to, nameof(to), out var toDate, out var toError))
        {
            query = query.Where(r => r.Start <= toDate);
        }
        else if (toError is not null)
        {
            return toError;
        }

        var reservations = await query
            .OrderByDescending(r => r.Start)
            .ThenBy(r => r.Room!.Name)
            .ToListAsync(ct);

        return Ok(reservations.Select(r => r.ToDto()).ToList());
    }

    /// <summary>Returns a single reservation.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationDto>> GetReservation(int id, CancellationToken ct)
    {
        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Room)
            .Include(r => r.Organizer)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return reservation is null
            ? NotFound(ApiErrorResponse.Single(ErrorCodes.ReservationNotFound, $"Reservation {id} does not exist."))
            : Ok(reservation.ToDto());
    }

    /// <summary>Creates a reservation. Rejected with rule codes when R1-R7 are broken.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationDto>> CreateReservation(
        [FromBody] CreateReservationRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return QueryFilters.BadRequest(ErrorCodes.ValidationFailed, "The request body is invalid.");
        }

        var result = await _reservations.CreateAsync(request, ct);
        return ToActionResult(result, nameof(GetReservation), new { id = result.Reservation?.Id });
    }

    /// <summary>Edits a reservation. The same rules apply as on create.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> UpdateReservation(
        int id,
        [FromBody] UpdateReservationRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return QueryFilters.BadRequest(ErrorCodes.ValidationFailed, "The request body is invalid.");
        }

        var result = await _reservations.UpdateAsync(id, request, ct);
        return ToActionResult(result);
    }

    /// <summary>Cancels a reservation. A cancelled reservation no longer blocks the room (R2).</summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> CancelReservation(int id, CancellationToken ct)
    {
        var result = await _reservations.CancelAsync(id, ct);
        return ToActionResult(result);
    }

    /// <summary>
    /// Ends a meeting early. The recorded end time and attendance feed the room usage report.
    /// </summary>
    [HttpPost("{id:int}/end")]
    [ProducesResponseType(typeof(ReservationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> EndMeeting(
        int id,
        [FromBody] EndMeetingRequest? request,
        CancellationToken ct)
    {
        var result = await _reservations.EndEarlyAsync(id, request ?? new EndMeetingRequest(null), ct);
        return ToActionResult(result);
    }

    private ActionResult<ReservationDto> ToActionResult(
        ReservationResult result,
        string? createdAction = null,
        object? routeValues = null)
    {
        if (result.Succeeded)
        {
            if (result.StatusCode == StatusCodes.Status201Created && createdAction is not null)
            {
                return CreatedAtAction(createdAction, routeValues, result.Reservation);
            }

            return Ok(result.Reservation);
        }

        var payload = ApiErrorResponse.FromRuleViolations(result.Errors);
        return result.StatusCode switch
        {
            StatusCodes.Status404NotFound => NotFound(payload),
            StatusCodes.Status409Conflict => Conflict(payload),
            _ => BadRequest(payload)
        };
    }
}
