using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Data;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Services;

/// <summary>
/// Keeps the stored status honest: a reservation whose end time has passed is transitioned
/// from Booked to Completed. Without this the status filter would show stale "Booked" rows
/// for meetings that already happened.
/// </summary>
public class ReservationStatusUpdater : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<ReservationStatusUpdater> _logger;

    public ReservationStatusUpdater(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<ReservationStatusUpdater> logger)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseFinishedReservationsAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to close finished reservations.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), _clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CloseFinishedReservationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _clock.GetUtcNow().UtcDateTime;

        var finished = await db.Reservations
            .Where(r => r.Status == ReservationStatus.Booked && r.End <= now)
            .ToListAsync(ct);

        if (finished.Count == 0)
        {
            return;
        }

        foreach (var reservation in finished)
        {
            reservation.Status = ReservationStatus.Completed;
            reservation.CompletedAt = reservation.End;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Closed {Count} finished reservation(s).", finished.Count);
    }
}
