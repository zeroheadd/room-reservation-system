using Microsoft.EntityFrameworkCore;
using RoomReservation.Api.Models;

namespace RoomReservation.Api.Data;

/// <summary>
/// EF Core context backed by the in-memory provider, as required by the task.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Room> Rooms => Set<Room>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Room>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Name).IsRequired().HasMaxLength(100);
            entity.Property(r => r.Code).IsRequired().HasMaxLength(20);
            entity.HasIndex(r => r.Code).IsUnique();
            entity.Property(r => r.Floor).IsRequired().HasMaxLength(100);

            // Store the equipment flags as a primitive collection so the in-memory provider
            // can translate Contains() queries used by the room filter.
            entity.PrimitiveCollection(r => r.Equipment);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.LastName).IsRequired().HasMaxLength(100);
            entity.Property(u => u.Code).IsRequired().HasMaxLength(20);
            entity.HasIndex(u => u.Code).IsUnique();
            entity.Ignore(u => u.FullName);
        });

        modelBuilder.Entity<Reservation>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Title).IsRequired().HasMaxLength(200);
            entity.Property(r => r.Notes).HasMaxLength(2000);
            entity.Ignore(r => r.Duration);

            entity.HasOne(r => r.Room)
                  .WithMany(r => r.Reservations)
                  .HasForeignKey(r => r.RoomId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(r => r.Organizer)
                  .WithMany(u => u.Reservations)
                  .HasForeignKey(r => r.OrganizerId)
                  .OnDelete(DeleteBehavior.Restrict);

            // The overlap checks (R2/R3) always filter on room/organizer plus a time window.
            entity.HasIndex(r => new { r.RoomId, r.Start, r.End });
            entity.HasIndex(r => new { r.OrganizerId, r.Start, r.End });
        });
    }
}
