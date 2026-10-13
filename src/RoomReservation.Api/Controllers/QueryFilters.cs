using Microsoft.AspNetCore.Mvc;
using RoomReservation.Api.Domain;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Controllers;

/// <summary>
/// Which reservations are "active" is used by several filters. Keeping the definition in one
/// place stops the API and the rules from drifting apart.
/// </summary>
public static class ReservationStatuses
{
    public static bool TryParse(string? value, out ReservationStatus status)
    {
        status = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (Enum.TryParse<ReservationStatus>(value, ignoreCase: true, out var parsed))
        {
            status = parsed;
            return true;
        }

        // Accept the friendly spellings the UI uses.
        return value.Trim().ToLowerInvariant() switch
        {
            "booked" or "active" => Set(ReservationStatus.Booked, out status),
            "completed" or "finished" => Set(ReservationStatus.Completed, out status),
            "cancelled" or "canceled" => Set(ReservationStatus.Cancelled, out status),
            _ => false
        };

        static bool Set(ReservationStatus value, out ReservationStatus status)
        {
            status = value;
            return true;
        }
    }
}

/// <summary>Helpers for turning raw query strings into strongly typed filters.</summary>
public static class QueryFilters
{
    /// <summary>Fails the request with a clear 400 payload instead of silently ignoring bad input.</summary>
    public static BadRequestObjectResult BadRequest(string code, string message) =>
        new(ApiErrorResponse.Single(code, message));

    public static bool TryParseDate(string? value, string parameterName, out DateTime result, out BadRequestObjectResult? error)
    {
        error = null;
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            error = BadRequest(ErrorCodes.ValidationFailed, $"'{value}' is not a valid date for '{parameterName}'. Use ISO 8601, e.g. 2026-01-31T09:00:00Z.");
            return false;
        }

        result = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    public static bool TryParseEquipment(string? value, out List<Equipment> equipment, out BadRequestObjectResult? error)
    {
        error = null;
        equipment = new List<Equipment>();

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.Replace(" ", string.Empty).ToLowerInvariant())
            {
                case "projector":
                    equipment.Add(Equipment.Projector);
                    break;
                case "tv":
                case "tvscreen":
                    equipment.Add(Equipment.TvScreen);
                    break;
                case "videoconference":
                case "videoconferencing":
                case "video":
                    equipment.Add(Equipment.VideoConference);
                    break;
                case "whiteboard":
                    equipment.Add(Equipment.Whiteboard);
                    break;
                default:
                    error = BadRequest(
                        ErrorCodes.ValidationFailed,
                        $"Unknown equipment '{part}'. Allowed values: Projector, TV screen, Video conferencing, Whiteboard.");
                    return false;
            }
        }

        return true;
    }
}
