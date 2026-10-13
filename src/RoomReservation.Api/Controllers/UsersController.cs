using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Controllers;

/// <summary>
/// Users page: list every user with their name, identifier and number of active reservations.
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>Lists users, optionally searched by name, code, e-mail or department.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetUsers(
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var query = _db.Users
            .AsNoTracking()
            .Include(u => u.Reservations)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                EF.Functions.Like(u.FirstName, $"%{term}%") ||
                EF.Functions.Like(u.LastName, $"%{term}%") ||
                EF.Functions.Like(u.Code, $"%{term}%") ||
                EF.Functions.Like(u.Email, $"%{term}%") ||
                EF.Functions.Like(u.Department, $"%{term}%"));
        }

        var users = await query
            .OrderBy(u => u.FirstName)
            .ThenBy(u => u.LastName)
            .ToListAsync(ct);

        return Ok(users.Select(u => u.ToDto()).ToList());
    }

    /// <summary>Returns a single user.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetUser(int id, CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Reservations)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        return user is null
            ? NotFound(ApiErrorResponse.Single(ErrorCodes.UserNotFound, $"User {id} does not exist."))
            : Ok(user.ToDto());
    }
}
