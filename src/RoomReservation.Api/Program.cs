using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RoomReservation.Api.Data;
using RoomReservation.Api.Models;
using RoomReservation.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuration - the numeric booking limits are bound from appsettings.json
// (BookingSettings section) so R1/R6 and the report thresholds are configurable.
// ---------------------------------------------------------------------------
builder.Services
    .AddOptions<BookingSettings>()
    .Bind(builder.Configuration.GetSection(BookingSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<BookingSettings>>().Value);

// ---------------------------------------------------------------------------
// Data - EF Core in-memory database, as required by the task.
// ---------------------------------------------------------------------------
var databaseName = builder.Configuration["Database:Name"] ?? "room-reservations";
builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));

// Testable clock: swap for a fake in unit tests.
builder.Services.AddSingleton(TimeProvider.System);

// ---------------------------------------------------------------------------
// Application services (constructor injection throughout).
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IBookingRuleValidator, BookingRuleValidator>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IRoomUsageReportService, RoomUsageReportService>();
builder.Services.AddSingleton<IRoomUsageReportRenderer, RoomUsageReportRenderer>();
builder.Services.AddHostedService<ReservationStatusUpdater>();

// ---------------------------------------------------------------------------
// API surface
// ---------------------------------------------------------------------------
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums travel as readable strings ("Booked", "Projector") instead of numbers.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Room reservation system",
        Version = "v1",
        Description = "Meeting room booking API with rules R1-R7 enforced in the backend."
    });
});

// The React dev server runs on its own origin during development.
const string DevCorsPolicy = "dev-client";
builder.Services.AddCors(options =>
{
    options.AddPolicy(DevCorsPolicy, policy => policy
        .WithOrigins(
            "http://localhost:5173",
            "http://127.0.0.1:5173",
            "http://localhost:4173",
            "http://127.0.0.1:4173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

// Seed the in-memory database so the app has something to show on first run. Integration tests
// turn this off (Database:Seed=false) so they can create their own deterministic fixtures.
if (builder.Configuration.GetValue("Database:Seed", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbSeeder.SeedAsync(db, clock, logger);
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "Room reservation API v1");
    options.DocumentTitle = "Room reservation API";
});

app.UseCors(DevCorsPolicy);

// Serve the built React app when it has been copied to wwwroot (see build.cmd), so a single
// process can host both halves of the application. During development the Vite dev server is
// used instead and this simply finds nothing.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Client side routing: any non-API path returns index.html so a refresh on /reservations works.
app.MapFallbackToFile("index.html");

app.MapGet("/", () => Results.Redirect("/swagger"))
   .ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .ExcludeFromDescription();

app.Run();

/// <summary>Exposed so the test project can boot the API with WebApplicationFactory.</summary>
public partial class Program;
