using RoomReservation.Api.Domain;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;
using Xunit;

namespace RoomReservation.Api.Tests;

/// <summary>
/// The configured limits in appsettings.json are what make rules R1 and R6 flexible, so the
/// binding of BookingSettings to real validation behaviour is pinned down here.
/// </summary>
public class BookingSettingsTests
{
    private static readonly DateTime Now = TestContextFactory.Now;

    [Fact]
    public void Defaults_MatchTheValuesShippedInAppsettings()
    {
        var settings = new BookingSettings();

        Assert.Equal(7, settings.MaxReservationDurationDays);
        Assert.Equal(90, settings.BookingHorizonDays);
        Assert.Equal(0.5, settings.UnderusedThreshold);
        Assert.Equal(3, settings.MinimumMeetingsForUsageVerdict);
    }

    [Theory]
    [InlineData(7, 7, true)]
    [InlineData(7, 7.001, false)]
    [InlineData(14, 14, true)]
    [InlineData(14, 15, false)]
    [InlineData(1, 0.5, true)]
    public async Task MaxDuration_IsInclusive(double configuredDays, double bookedDays, bool shouldBeAllowed)
    {
        using var db = TestContextFactory.CreateContext();
        var seed = TestContextFactory.Seed(db);

        var settings = TestContextFactory.DefaultSettings();
        settings.MaxReservationDurationDays = (int)configuredDays;

        var validator = new BookingRuleValidator(db, new FixedTimeProvider(Now), settings);
        var errors = await validator.ValidateAsync(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Duration probe",
            Start = Now.AddDays(1),
            End = Now.AddDays(1).AddDays(bookedDays),
            Attendees = 2
        });

        var rejected = errors.Any(e => e.Code == ErrorCodes.MaxDurationExceeded);
        Assert.Equal(!shouldBeAllowed, rejected);
    }

    [Theory]
    [InlineData(90, 90, true)]
    [InlineData(90, 90.001, false)]
    [InlineData(30, 30, true)]
    [InlineData(30, 31, false)]
    [InlineData(365, 300, true)]
    public async Task BookingHorizon_IsInclusive(double configuredDays, double daysAhead, bool shouldBeAllowed)
    {
        using var db = TestContextFactory.CreateContext();
        var seed = TestContextFactory.Seed(db);

        var settings = TestContextFactory.DefaultSettings();
        settings.BookingHorizonDays = (int)configuredDays;

        var validator = new BookingRuleValidator(db, new FixedTimeProvider(Now), settings);
        var errors = await validator.ValidateAsync(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Horizon probe",
            Start = Now.AddDays(daysAhead),
            End = Now.AddDays(daysAhead).AddHours(1),
            Attendees = 2
        });

        var rejected = errors.Any(e => e.Code == ErrorCodes.BookingHorizonExceeded);
        Assert.Equal(!shouldBeAllowed, rejected);
    }

    [Fact]
    public async Task RuleMessages_MentionTheConfiguredLimits()
    {
        using var db = TestContextFactory.CreateContext();
        var seed = TestContextFactory.Seed(db);

        var settings = TestContextFactory.DefaultSettings();
        settings.MaxReservationDurationDays = 3;
        settings.BookingHorizonDays = 10;

        var validator = new BookingRuleValidator(db, new FixedTimeProvider(Now), settings);
        var errors = await validator.ValidateAsync(new Reservation
        {
            RoomId = seed.Small.Id,
            OrganizerId = seed.Organizer.Id,
            Title = "Both limits",
            Start = Now.AddDays(11),
            End = Now.AddDays(20),
            Attendees = 2
        });

        Assert.Contains(errors, e => e.Code == ErrorCodes.MaxDurationExceeded && e.Message.Contains("3 days"));
        Assert.Contains(errors, e => e.Code == ErrorCodes.BookingHorizonExceeded && e.Message.Contains("10 days"));
    }
}
