using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Data;

/// <summary>
/// Fills the in-memory database with a small but realistic office: rooms with different
/// capacity and equipment, users, and a history of reservations. The history deliberately
/// contains one room that is booked a lot but barely attended, so the usage report has
/// something to show.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, TimeProvider clock, ILogger logger, bool force = false)
    {
        if (!force && await db.Rooms.AnyAsync())
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var at = TimeZoneInfo.Local;
        var hour = TimeSpan.FromHours(1);

        var rooms = new List<Room>
        {
            new() { Name = "Aurora", Code = "KA-201", Capacity = 12, Floor = "2nd floor, west wing", IsActive = true,
                    Equipment = new List<Equipment> { Equipment.Projector, Equipment.TvScreen, Equipment.Whiteboard } },
            new() { Name = "Bastion", Code = "KA-202", Capacity = 6, Floor = "2nd floor, west wing", IsActive = true,
                    Equipment = new List<Equipment> { Equipment.TvScreen, Equipment.VideoConference } },
            new() { Name = "Cascade", Code = "KA-305", Capacity = 30, Floor = "3rd floor, auditorium", IsActive = true,
                    Equipment = new List<Equipment> { Equipment.Projector, Equipment.VideoConference, Equipment.Whiteboard } },
            new() { Name = "Delta", Code = "KA-310", Capacity = 4, Floor = "3rd floor, east wing", IsActive = true,
                    Equipment = new List<Equipment> { Equipment.Whiteboard } },
            new() { Name = "Everest", Code = "KA-104", Capacity = 20, Floor = "1st floor, lobby side", IsActive = true,
                    Equipment = new List<Equipment> { Equipment.Projector, Equipment.VideoConference, Equipment.TvScreen, Equipment.Whiteboard } },
            new() { Name = "Fjord", Code = "KA-108", Capacity = 8, Floor = "1st floor, lobby side", IsActive = false,
                    Equipment = new List<Equipment> { Equipment.Projector, Equipment.Whiteboard } }
        };

        var users = new List<User>
        {
            new() { FirstName = "Auste", LastName = "Kazlauskaite", Code = "EMP-0001", Email = "auste.kazlauskaite@example.com", Department = "Engineering" },
            new() { FirstName = "Mantas", LastName = "Petrauskas", Code = "EMP-0002", Email = "mantas.petrauskas@example.com", Department = "Product" },
            new() { FirstName = "Ruta", LastName = "Jankauskaite", Code = "EMP-0003", Email = "ruta.jankauskaite@example.com", Department = "Design" },
            new() { FirstName = "Tomas", LastName = "Vaitkus", Code = "EMP-0004", Email = "tomas.vaitkus@example.com", Department = "Engineering" },
            new() { FirstName = "Ieva", LastName = "Stankeviciute", Code = "EMP-0005", Email = "ieva.stankeviciute@example.com", Department = "People & Culture" },
            new() { FirstName = "Darius", LastName = "Mockus", Code = "EMP-0006", Email = "darius.mockus@example.com", Department = "Finance" }
        };

        db.Rooms.AddRange(rooms);
        db.Users.AddRange(users);
        await db.SaveChangesAsync();

        var aurora = rooms[0];
        var bastion = rooms[1];
        var cascade = rooms[2];
        var delta = rooms[3];
        var everest = rooms[4];
        var fjord = rooms[5];

        var auste = users[0];
        var mantas = users[1];
        var ruta = users[2];
        var tomas = users[3];
        var ieva = users[4];
        var darius = users[5];

        var reservations = new List<Reservation>();

        // A helper that builds a meeting on a given day offset from "today" at a local time.
        Reservation Meeting(
            Room room,
            User organizer,
            string title,
            int dayOffset,
            double startHour,
            double durationHours,
            int attendees,
            ReservationStatus status = ReservationStatus.Booked,
            int? actualAttendees = null,
            string? notes = null)
        {
            var localStart = now.Date.AddDays(dayOffset).AddHours(startHour);
            var start = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localStart, DateTimeKind.Unspecified), at);
            var end = start.AddHours(durationHours);

            return new Reservation
            {
                RoomId = room.Id,
                OrganizerId = organizer.Id,
                Title = title,
                Notes = notes,
                Start = start,
                End = end,
                Attendees = attendees,
                Status = status,
                CreatedAt = now.AddDays(dayOffset - 10),
                ActualAttendees = actualAttendees,
                CompletedAt = status == ReservationStatus.Completed ? end : null,
                CancelledAt = status == ReservationStatus.Cancelled ? now.AddDays(dayOffset - 2) : null
            };
        }

        // ---- History: Aurora is booked constantly but people rarely turn up. ----
        reservations.Add(Meeting(aurora, auste, "Daily standup", -21, 9, 0.5, 10, ReservationStatus.Completed, 9));
        reservations.Add(Meeting(aurora, mantas, "Sprint planning", -20, 10, 2, 12, ReservationStatus.Completed, 4));
        reservations.Add(Meeting(aurora, ruta, "Design critique", -19, 14, 1.5, 8, ReservationStatus.Completed, 2));
        reservations.Add(Meeting(aurora, tomas, "Architecture sync", -18, 11, 2, 10, ReservationStatus.Completed, 3));
        reservations.Add(Meeting(aurora, ieva, "Onboarding session", -15, 9, 1, 6, ReservationStatus.Completed, 1));
        reservations.Add(Meeting(aurora, darius, "Budget review", -14, 15, 1.5, 9, ReservationStatus.Completed, 3));
        reservations.Add(Meeting(aurora, auste, "Retrospective", -13, 16, 1, 11, ReservationStatus.Completed, 5));
        reservations.Add(Meeting(aurora, mantas, "Roadmap workshop", -12, 10, 3, 12, ReservationStatus.Completed, 2));
        reservations.Add(Meeting(aurora, ruta, "User research readout", -11, 13, 1, 7, ReservationStatus.Completed, 0));
        reservations.Add(Meeting(aurora, tomas, "Code review club", -8, 17, 1, 6, ReservationStatus.Completed, 2));
        reservations.Add(Meeting(aurora, ieva, "Team lunch planning", -6, 12, 1, 5, ReservationStatus.Completed, 1));
        reservations.Add(Meeting(aurora, auste, "Incident postmortem", -4, 9, 1.5, 10, ReservationStatus.Completed, 4));

        // ---- Cascade: the big auditorium, used properly. ----
        reservations.Add(Meeting(cascade, ieva, "All-hands", -30, 15, 1.5, 30, ReservationStatus.Completed, 28));
        reservations.Add(Meeting(cascade, mantas, "Quarterly review", -16, 10, 2, 25, ReservationStatus.Completed, 22));
        reservations.Add(Meeting(cascade, darius, "Investor update", -9, 14, 1.5, 20, ReservationStatus.Completed, 19));
        reservations.Add(Meeting(cascade, auste, "Tech talk: EF Core", -5, 16, 1, 24, ReservationStatus.Completed, 21));

        // ---- Bastion: small room, well used. ----
        reservations.Add(Meeting(bastion, ruta, "Pair design", -7, 11, 1, 3, ReservationStatus.Completed, 3));
        reservations.Add(Meeting(bastion, tomas, "Vendor call", -3, 9, 1, 4, ReservationStatus.Completed, 4));
        reservations.Add(Meeting(bastion, auste, "1:1 with manager", -2, 13, 0.5, 2, ReservationStatus.Completed, 2));

        // ---- Delta and Everest: light but healthy usage. ----
        reservations.Add(Meeting(delta, darius, "Invoice triage", -6, 10, 1, 2, ReservationStatus.Completed, 2));
        reservations.Add(Meeting(delta, ieva, "Recruiting screen", -1, 14, 1, 3, ReservationStatus.Completed, 2));
        reservations.Add(Meeting(everest, mantas, "Customer workshop", -10, 9, 4, 18, ReservationStatus.Completed, 16));
        reservations.Add(Meeting(everest, auste, "Training: clean code", -5, 13, 2, 15, ReservationStatus.Completed, 12));

        // ---- A cancelled reservation that must not block the room (R2). ----
        reservations.Add(Meeting(fjord, ruta, "Moodboard session", 2, 10, 1, 4, ReservationStatus.Cancelled));

        // ---- Upcoming fixtures so the UI has something to show and act on. ----
        reservations.Add(Meeting(aurora, auste, "Sprint review", 1, 10, 1.5, 12, notes: "Bring the demo build."));
        reservations.Add(Meeting(cascade, ieva, "Company update", 2, 15, 1, 30));
        reservations.Add(Meeting(bastion, tomas, "Interview: backend dev", 3, 9, 1, 4));
        reservations.Add(Meeting(everest, mantas, "Partner kickoff", 4, 11, 2, 18));
        reservations.Add(Meeting(delta, darius, "Monthly closing", 5, 13, 2, 3));
        reservations.Add(Meeting(cascade, ruta, "Design system launch", 6, 14, 2, 26));

        db.Reservations.AddRange(reservations);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Seeded {Rooms} rooms, {Users} users and {Reservations} reservations.",
            rooms.Count, users.Count, reservations.Count);
    }
}
