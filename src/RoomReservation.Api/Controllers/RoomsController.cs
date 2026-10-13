using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Controllers;

/// <summary>
/// Rooms page: list every meeting room and allow filtering by minimum capacity,
/// equipment and floor.
/// </summary>
[ApiController]
[Route("api/rooms")]
[Produces("application/json")]
public class RoomsController : ControllerBase
{
    private readonly AppDbContext _db;

    public RoomsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists rooms, optionally filtered.</summary>
    /// <param name="minCapacity">Only rooms seating at least this many people.</param>
    /// <param name="equipment">Comma separated equipment the room must have all of, e.g. "Projector,Whiteboard".</param>
    /// <param name="floor">Case insensitive floor filter, partial match.</param>
    /// <param name="activeOnly">When true, hides inactive rooms.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoomDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<RoomDto>>> GetRooms(
        [FromQuery] int? minCapacity,
        [FromQuery] string? equipment,
        [FromQuery] string? floor,
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        if (minCapacity is < 0)
        {
            return QueryFilters.BadRequest(ErrorCodes.ValidationFailed, "minCapacity cannot be negative.");
        }

        var query = _db.Rooms
            .AsNoTracking()
            .Include(r => r.Reservations)
            .AsQueryable();

        if (minCapacity is not null)
        {
            query = query.Where(r => r.Capacity >= minCapacity);
        }

        if (QueryFilters.TryParseEquipment(equipment, out var required, out var error))
        {
            // The room must have every requested item, not just one of them.
            foreach (var item in required.Distinct())
            {
                var wanted = item;
                query = query.Where(r => r.Equipment.Contains(wanted));
            }
        }
        else if (error is not null)
        {
            return error;
        }

        if (!string.IsNullOrWhiteSpace(floor))
        {
            var floorFilter = floor.Trim();
            query = query.Where(r => EF.Functions.Like(r.Floor, $"%{floorFilter}%"));
        }

        if (activeOnly)
        {
            query = query.Where(r => r.IsActive);
        }

        var rooms = await query
            .OrderBy(r => r.Floor)
            .ThenBy(r => r.Name)
            .ToListAsync(ct);

        return Ok(rooms.Select(r => r.ToDto()).ToList());
    }

    /// <summary>Returns a single room.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoomDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDto>> GetRoom(int id, CancellationToken ct)
    {
        var room = await _db.Rooms
            .AsNoTracking()
            .Include(r => r.Reservations)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        return room is null
            ? NotFound(ApiErrorResponse.Single(ErrorCodes.RoomNotFound, $"Room {id} does not exist."))
            : Ok(room.ToDto());
    }

    /// <summary>The equipment vocabulary the UI can offer in its filters.</summary>
    [HttpGet("/api/equipment")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetEquipment() =>
        Ok(Enum.GetValues<Equipment>().Select(e => e.ToApiValue()).ToList());
}
