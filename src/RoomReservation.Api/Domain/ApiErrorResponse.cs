namespace RoomReservation.Api.Domain;

using RoomReservation.Api.Dtos;

/// <summary>
/// Stable, machine readable error codes returned by the API. The frontend can switch on
/// these instead of parsing messages.
/// </summary>
public static class ErrorCodes
{
    public const string ReservationNotFound = "RESERVATION_NOT_FOUND";
    public const string RoomNotFound = "ROOM_NOT_FOUND";
    public const string UserNotFound = "USER_NOT_FOUND";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ReservationNotModifiable = "RESERVATION_NOT_MODIFIABLE";
    public const string ReservationNotCancellable = "RESERVATION_NOT_CANCELLABLE";
    public const string ReservationNotEndable = "RESERVATION_NOT_ENDABLE";
    public const string ReservationNotStarted = "RESERVATION_NOT_STARTED";

    // Booking rules R1 - R7
    public const string MaxDurationExceeded = "R1_MAX_DURATION_EXCEEDED";
    public const string RoomDoubleBooked = "R2_ROOM_OVERLAP";
    public const string OrganizerDoubleBooked = "R3_ORGANIZER_OVERLAP";
    public const string InvalidTimeRange = "R4_INVALID_TIME_RANGE";
    public const string CapacityExceeded = "R5_CAPACITY_EXCEEDED";
    public const string BookingHorizonExceeded = "R6_BOOKING_HORIZON_EXCEEDED";
    public const string RoomInactive = "R7_ROOM_INACTIVE";
}

/// <summary>A single rule violation: a stable code plus a human readable message.</summary>
public record ValidationError(string Code, string Message);

/// <summary>
/// The uniform error envelope every failing endpoint returns, so the frontend always has
/// a code and a message to show.
/// </summary>
public record ApiErrorResponse(string Code, string Message, IReadOnlyList<ValidationError> Errors)
{
    public static ApiErrorResponse Single(string code, string message) =>
        new(code, message, new[] { new ValidationError(code, message) });

    /// <summary>
    /// Builds the envelope for one or more rule violations. When a single problem is reported the
    /// envelope itself carries that specific code, so clients can branch on the response code
    /// without walking the list; multiple problems are grouped under VALIDATION_FAILED.
    /// </summary>
    public static ApiErrorResponse FromRuleViolations(IReadOnlyList<ValidationError> errors) =>
        new(
            errors.Count == 1 ? errors[0].Code : ErrorCodes.ValidationFailed,
            errors.Count == 1
                ? errors[0].Message
                : $"The reservation breaks {errors.Count} booking rules.",
            errors);
}

/// <summary>
/// Every state changing reservation operation goes through this result type: either the
/// saved reservation, or the rule violations that stopped it plus the HTTP status to use.
/// </summary>
public record ReservationResult(ReservationDto? Reservation, IReadOnlyList<ValidationError> Errors, int StatusCode)
{
    public bool Succeeded => Errors.Count == 0 && Reservation is not null;

    public static ReservationResult Ok(ReservationDto reservation) => new(reservation, Array.Empty<ValidationError>(), 200);

    public static ReservationResult Created(ReservationDto reservation) => new(reservation, Array.Empty<ValidationError>(), 201);

    /// <summary>A booking rule was broken - always reported as 400 with the rule codes.</summary>
    public static ReservationResult RuleViolation(IReadOnlyList<ValidationError> errors) => new(null, errors, 400);

    /// <summary>The request referenced something that does not exist.</summary>
    public static ReservationResult NotFound(string code, string message) =>
        new(null, new[] { new ValidationError(code, message) }, 404);

    /// <summary>A state conflict, e.g. cancelling an already cancelled reservation.</summary>
    public static ReservationResult Conflict(string code, string message) =>
        new(null, new[] { new ValidationError(code, message) }, 409);
}
