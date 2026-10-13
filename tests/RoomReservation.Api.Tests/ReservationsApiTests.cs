using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RoomReservation.Api.Data;
using RoomReservation.Api.Dtos;
using RoomReservation.Api.Models;
using Xunit;

namespace RoomReservation.Api.Tests;

/// <summary>
/// A deterministic fixture for the end-to-end tests: one active room, one inactive room and
/// two users, with no pre-existing reservations. The demo seed data is turned off so a test
/// never collides with a seeded meeting.
/// </summary>
public static class ApiFixtures
{
    public static void Seed(AppDbContext db)
    {
        if (db.Rooms.Any())
        {
            return;
        }

        db.Rooms.AddRange(
            new Room
            {
                Name = "Aurora",
                Code = "KA-201",
                Capacity = 10,
                Floor = "2nd floor, west wing",
                IsActive = true,
                Equipment = new List<Equipment> { Equipment.Projector, Equipment.Whiteboard }
            },
            new Room
            {
                Name = "Cascade",
                Code = "KA-305",
                Capacity = 30,
                Floor = "3rd floor, auditorium",
                IsActive = true,
                Equipment = new List<Equipment> { Equipment.Projector, Equipment.TvScreen, Equipment.VideoConference }
            },
            new Room
            {
                Name = "Fjord",
                Code = "KA-108",
                Capacity = 8,
                Floor = "1st floor, lobby side",
                IsActive = false,
                Equipment = new List<Equipment> { Equipment.Projector }
            });

        db.Users.AddRange(
            new User { FirstName = "Auste", LastName = "Kazlauskaite", Code = "EMP-0001", Email = "auste@example.com", Department = "Engineering" },
            new User { FirstName = "Mantas", LastName = "Petrauskas", Code = "EMP-0002", Email = "mantas@example.com", Department = "Product" });

        db.SaveChanges();
    }
}

/// <summary>Shared setup for the HTTP-level tests: a private database and JSON options.</summary>
public abstract class ApiTestBase : IClassFixture<ApiTestBase.Factory>
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected readonly Factory AppFactory;

    protected ApiTestBase(Factory factory)
    {
        AppFactory = factory;
    }

    /// <summary>
    /// Boots the real API. Every factory run gets its own in-memory database and the demo seed
    /// data is off, so each test starts from fixtures it fully controls.
    /// </summary>
    public class Factory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"api-tests-{Guid.NewGuid()}";
        private bool _seeded;
        private readonly object _seedLock = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Name", _databaseName);
            builder.UseSetting("Database:Seed", "false");
            builder.UseEnvironment("Testing");
        }

        public void EnsureFixtures()
        {
            lock (_seedLock)
            {
                if (_seeded)
                {
                    return;
                }

                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                ApiFixtures.Seed(db);
                _seeded = true;
            }
        }
    }

    protected HttpClient CreateClient()
    {
        AppFactory.EnsureFixtures();
        return AppFactory.CreateClient();
    }

    protected static async Task<(RoomDto Room, RoomDto LargeRoom, UserDto Organizer)> PickAsync(HttpClient client)
    {
        var rooms = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms", Json);
        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users", Json);
        return (rooms!.First(r => r.IsActive), rooms!.Last(r => r.IsActive), users!.First());
    }

    /// <summary>A time slot nothing else uses, safely inside the booking horizon.</summary>
    protected static (DateTime Start, DateTime End) FreeSlot(int daysAhead, double hours = 1) =>
        (DateTime.UtcNow.AddDays(daysAhead).Date.AddHours(9), DateTime.UtcNow.AddDays(daysAhead).Date.AddHours(9 + hours));
}

/// <summary>
/// End-to-end checks through the real HTTP pipeline: status codes, the error envelope with rule
/// codes, filtering, and the report endpoints.
/// </summary>
public class ReservationsApiTests : ApiTestBase
{
    public ReservationsApiTests(Factory factory) : base(factory)
    {
    }

    [Fact]
    public async Task FixtureData_IsAvailableThroughTheApi()
    {
        var client = CreateClient();

        var rooms = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms", Json);
        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users", Json);

        Assert.NotNull(rooms);
        Assert.Equal(3, rooms!.Count);
        Assert.Contains(rooms!, r => !r.IsActive);
        Assert.Equal(2, users!.Count);
        Assert.All(users!, u => Assert.False(string.IsNullOrWhiteSpace(u.FullName)));
    }

    [Fact]
    public async Task Rooms_FilterByCapacityEquipmentFloorAndStatus()
    {
        var client = CreateClient();

        var byCapacity = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms?minCapacity=20", Json);
        Assert.All(byCapacity!, r => Assert.True(r.Capacity >= 20));

        var byEquipment = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms?equipment=Projector,Whiteboard", Json);
        Assert.NotEmpty(byEquipment!);
        Assert.All(byEquipment!, r =>
        {
            Assert.Contains("Projector", r.Equipment);
            Assert.Contains("Whiteboard", r.Equipment);
        });

        var byFloor = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms?floor=3rd", Json);
        Assert.All(byFloor!, r => Assert.Contains("3rd", r.Floor));

        var activeOnly = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms?activeOnly=true", Json);
        Assert.All(activeOnly!, r => Assert.True(r.IsActive));
    }

    [Fact]
    public async Task Rooms_UnknownEquipment_ReturnsBadRequestWithACode()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/rooms?equipment=Hologram");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorPayload>(Json);
        Assert.Equal("VALIDATION_FAILED", error!.Code);
    }

    [Fact]
    public async Task Reserving_InactiveRoom_ReturnsTheR7Code()
    {
        var client = CreateClient();
        var rooms = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms", Json);
        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users", Json);
        var inactive = rooms!.First(r => !r.IsActive);
        var (start, end) = FreeSlot(1);

        var response = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = inactive.Id,
            organizerId = users!.First().Id,
            title = "Should be refused",
            start,
            end,
            attendees = 2
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorPayload>(Json);
        Assert.Contains(error!.Errors, e => e.Code == "R7_ROOM_INACTIVE");
    }

    [Fact]
    public async Task Reserving_TooManyAttendees_ReturnsTheR5Code()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);
        var (start, end) = FreeSlot(1);

        var response = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "Crowded",
            start,
            end,
            attendees = room.Capacity + 5
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorPayload>(Json);
        Assert.Contains(error!.Errors, e => e.Code == "R5_CAPACITY_EXCEEDED");
    }

    [Fact]
    public async Task Reserving_OverlappingSlotInTheSameRoom_ReturnsTheR2Code()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);
        var (start, end) = FreeSlot(2);
        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users", Json);

        var first = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "First",
            start,
            end,
            attendees = 2
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = users!.Last().Id,
            title = "Second, overlapping",
            start = start.AddMinutes(30),
            end = end.AddMinutes(30),
            attendees = 2
        });

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ApiErrorPayload>(Json);
        Assert.Contains(error!.Errors, e => e.Code == "R2_ROOM_OVERLAP");
    }

    [Fact]
    public async Task Create_CancelAndEndMeeting_WorkEndToEnd()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);
        var (start, end) = FreeSlot(3);

        var create = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "Lifecycle check",
            start,
            end,
            attendees = 2
        });

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<ReservationDto>(Json);
        Assert.NotNull(created);
        Assert.Equal("Booked", created!.Status);

        var cancel = await client.PostAsync($"/api/reservations/{created.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var cancelled = await cancel.Content.ReadFromJsonAsync<ReservationDto>(Json);
        Assert.Equal("Cancelled", cancelled!.Status);

        // A cancelled reservation can no longer be cancelled or edited.
        var cancelAgain = await client.PostAsync($"/api/reservations/{created.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, cancelAgain.StatusCode);

        var edit = await client.PutAsJsonAsync($"/api/reservations/{created.Id}", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "Edited after cancel",
            start = start.AddDays(1),
            end = end.AddDays(1),
            attendees = 2
        });
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task Update_Reservation_MovesTheMeeting()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);
        var (start, end) = FreeSlot(4);

        var create = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "Before edit",
            start,
            end,
            attendees = 2
        });
        Assert.True(create.StatusCode == HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var created = await create.Content.ReadFromJsonAsync<ReservationDto>(Json);

        var movedStart = start.AddDays(1);
        var update = await client.PutAsJsonAsync($"/api/reservations/{created!.Id}", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "After edit",
            start = movedStart,
            end = movedStart.AddHours(2),
            attendees = 3
        });

        Assert.True(update.StatusCode == HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = await update.Content.ReadFromJsonAsync<ReservationDto>(Json);
        Assert.Equal("After edit", updated!.Title);
        Assert.Equal(2, updated.DurationHours);
        Assert.Equal(3, updated.Attendees);
    }

    [Fact]
    public async Task Update_MovingAMeetingOntoAnother_ReturnsTheR2Code()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);
        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users", Json);
        var (start, end) = FreeSlot(6);

        var first = await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "Occupies the slot",
            start,
            end,
            attendees = 2
        });
        var firstReservation = await first.Content.ReadFromJsonAsync<ReservationDto>(Json);

        await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = users!.Last().Id,
            title = "Will be moved",
            start = start.AddHours(3),
            end = end.AddHours(3),
            attendees = 2
        });

        var secondId = (await client.GetFromJsonAsync<List<ReservationDto>>("/api/reservations", Json))!
            .Single(r => r.Title == "Will be moved").Id;

        var update = await client.PutAsJsonAsync($"/api/reservations/{secondId}", new
        {
            roomId = room.Id,
            organizerId = users!.Last().Id,
            title = "Will be moved",
            start,
            end,
            attendees = 2
        });

        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var error = await update.Content.ReadFromJsonAsync<ApiErrorPayload>(Json);
        Assert.Contains(error!.Errors, e => e.Code == "R2_ROOM_OVERLAP");
        Assert.NotNull(firstReservation);
    }

    [Fact]
    public async Task Reservations_FilterByRoomOrganizerStatusAndDateRange()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);
        var (start, end) = FreeSlot(7);

        await client.PostAsJsonAsync("/api/reservations", new
        {
            roomId = room.Id,
            organizerId = organizer.Id,
            title = "Filterable",
            start,
            end,
            attendees = 2
        });

        var cancelled = await client.GetFromJsonAsync<List<ReservationDto>>("/api/reservations?status=Cancelled", Json);
        Assert.NotNull(cancelled);
        Assert.All(cancelled!, r => Assert.Equal("Cancelled", r.Status));

        var byRoom = await client.GetFromJsonAsync<List<ReservationDto>>($"/api/reservations?roomId={room.Id}", Json);
        Assert.All(byRoom!, r => Assert.Equal(room.Id, r.RoomId));

        var byOrganizer = await client.GetFromJsonAsync<List<ReservationDto>>($"/api/reservations?organizerId={organizer.Id}", Json);
        Assert.All(byOrganizer!, r => Assert.Equal(organizer.Id, r.OrganizerId));

        var window = await client.GetFromJsonAsync<List<ReservationDto>>(
            $"/api/reservations?from={start.AddHours(-1):O}&to={end.AddHours(1):O}", Json);
        Assert.Contains(window!, r => r.Title == "Filterable");
    }

    [Fact]
    public async Task Reservations_UnknownStatus_ReturnsBadRequest()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/reservations?status=Postponed");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingReservation_Returns404WithACode()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/api/reservations/987654");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorPayload>(Json);
        Assert.Equal("RESERVATION_NOT_FOUND", error!.Code);
    }

    [Fact]
    public async Task RoomUsageReport_CountsFinishedMeetingsAndFlagsUnderusedRooms()
    {
        var client = CreateClient();
        var (room, _, organizer) = await PickAsync(client);

        // Four finished meetings with poor attendance, recorded through the real endpoints by
        // creating meetings in the past is impossible (R4), so the report is checked with data
        // written straight into the store, exactly as the seeder would.
        AppFactory.EnsureFixtures();
        using (var scope = AppFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var day = 1; day <= 4; day++)
            {
                var past = DateTime.UtcNow.AddDays(-day).Date.AddHours(10);
                db.Reservations.Add(new Reservation
                {
                    RoomId = room.Id,
                    OrganizerId = organizer.Id,
                    Title = $"Past meeting {day}",
                    Start = past,
                    End = past.AddHours(2),
                    Attendees = 5,
                    ActualAttendees = 1,
                    Status = ReservationStatus.Completed,
                    CompletedAt = past.AddHours(2)
                });
            }

            db.SaveChanges();
        }

        var report = await client.GetFromJsonAsync<RoomUsageReportDto>("/api/reports/room-usage", Json);

        Assert.NotNull(report);
        var metrics = report!.Rooms.Single(r => r.RoomId == room.Id);
        Assert.Equal(4, metrics.CompletedMeetings);
        Assert.True(metrics.IsUnderused);
        Assert.Contains(report.Rooms, r => r.IsUnderused);

        var html = await client.GetStringAsync("/api/reports/room-usage/print");
        Assert.Contains("Room usage report", html);
        Assert.Contains("Save as PDF", html);
    }

    [Fact]
    public async Task Settings_ExposeTheConfigurableLimits()
    {
        var client = CreateClient();

        var settings = await client.GetFromJsonAsync<BookingSettings>("/api/settings", Json);

        Assert.NotNull(settings);
        Assert.Equal(7, settings!.MaxReservationDurationDays);
        Assert.Equal(90, settings.BookingHorizonDays);
    }

    // The error envelope as the client sees it: a code, a message and rule violations.
    protected record ApiErrorPayload(string Code, string Message, List<RuleError> Errors);

    protected record RuleError(string Code, string Message);
}

/// <summary>
/// A second factory that keeps the demo seeder enabled, so the shipped sample data can be
/// asserted on independently of the deterministic fixtures above.
/// </summary>
public class SeedDataTests : IClassFixture<SeedDataTests.SeededFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly SeededFactory _factory;

    public SeedDataTests(SeededFactory factory) => _factory = factory;

    public class SeededFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Name", $"seed-tests-{Guid.NewGuid()}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseEnvironment("Testing");
        }
    }

    [Fact]
    public async Task SeededData_PopulatesRoomsUsersReservationsAndAnUnderusedRoom()
    {
        var client = _factory.CreateClient();

        var rooms = await client.GetFromJsonAsync<List<RoomDto>>("/api/rooms", Json);
        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users", Json);
        var reservations = await client.GetFromJsonAsync<List<ReservationDto>>("/api/reservations", Json);
        var report = await client.GetFromJsonAsync<RoomUsageReportDto>("/api/reports/room-usage", Json);

        Assert.Equal(6, rooms!.Count);
        Assert.Equal(6, users!.Count);
        Assert.NotEmpty(reservations!);
        Assert.Contains(reservations!, r => r.Status == "Cancelled");
        Assert.Contains(reservations!, r => r.Status == "Completed");
        Assert.Contains(reservations!, r => r.Status == "Booked");

        // The sample office deliberately contains a room that is booked often but rarely used.
        Assert.NotNull(report);
        Assert.Contains(report!.Rooms, r => r.IsUnderused);
        Assert.True(report.Summary.UnderusedRooms >= 1);
        Assert.True(report.Summary.WastedHours > 0);
    }
}
