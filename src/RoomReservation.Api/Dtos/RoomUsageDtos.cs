namespace RoomReservation.Api.Dtos;

/// <summary>Usage metrics for a single room.</summary>
public record RoomUsageDto(
    int RoomId,
    string RoomName,
    string RoomCode,
    string Floor,
    int Capacity,
    bool IsActive,
    int TotalReservations,
    int ActiveReservations,
    int CompletedMeetings,
    int CancelledReservations,
    double ReservedHours,
    double ActualHours,
    double WastedHours,
    int BookedAttendees,
    int ActualAttendees,
    int MeetingsWithAttendance,
    double? Utilization,
    bool IsUnderused,
    string Verdict);

/// <summary>Totals across every room.</summary>
public record RoomUsageSummaryDto(
    int Rooms,
    int TotalReservations,
    int CompletedMeetings,
    int CancelledReservations,
    double ReservedHours,
    double ActualHours,
    double WastedHours,
    double Utilization,
    int UnderusedRooms);

/// <summary>The full room usage report, sorted worst utilization first.</summary>
public record RoomUsageReportDto(
    DateTime GeneratedAt,
    int? LastDays,
    double UnderusedThreshold,
    int MinimumMeetingsForUsageVerdict,
    RoomUsageSummaryDto Summary,
    IReadOnlyList<RoomUsageDto> Rooms);
