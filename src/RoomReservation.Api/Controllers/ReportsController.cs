using Microsoft.AspNetCore.Mvc;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;

namespace RoomReservation.Api.Controllers;

/// <summary>
/// Room usage report: shows which rooms are booked often but actually used very little.
/// <c>/api/reports/room-usage</c> returns JSON for the UI, <c>/print</c> renders the printable
/// document the browser turns into a PDF.
/// </summary>
[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public class ReportsController : ControllerBase
{
    private readonly IRoomUsageReportService _report;

    public ReportsController(IRoomUsageReportService report)
    {
        _report = report;
    }

    /// <summary>Room usage figures per room.</summary>
    /// <param name="lastDays">Limit the report to reservations that started within the last N days.</param>
    [HttpGet("room-usage")]
    [ProducesResponseType(typeof(RoomUsageReportDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoomUsageReportDto>> GetRoomUsage(
        [FromQuery] int? lastDays,
        CancellationToken ct)
    {
        if (lastDays is <= 0)
        {
            return QueryFilters.BadRequest(ErrorCodes.ValidationFailed, "lastDays must be a positive number of days.");
        }

        return Ok(await _report.BuildAsync(lastDays, ct));
    }

    /// <summary>
    /// The same report as a self-contained HTML page that opens the browser print dialog,
    /// which is how the PDF export is produced.
    /// </summary>
    [HttpGet("room-usage/print")]
    [Produces("text/html")]
    public async Task<ContentResult> GetRoomUsagePrintable([FromQuery] int? lastDays, CancellationToken ct)
    {
        var html = await _report.RenderHtmlAsync(lastDays is > 0 ? lastDays : null, ct);
        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Exposes the configurable limits so the UI can explain the rules to the user.</summary>
    [HttpGet("/api/settings")]
    [ProducesResponseType(typeof(BookingSettings), StatusCodes.Status200OK)]
    public ActionResult<BookingSettings> GetSettings([FromServices] BookingSettings settings) => Ok(settings);
}
