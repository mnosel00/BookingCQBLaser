using BookingCQBLaser.Api.Filters;
using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Infrastructure.BackgroundJobs;
using BookingCQBLaser.Infrastructure.ExternalServices;
using BookingCQBLaser.Infrastructure.ExternalServices.PGateway;
using BookingCQBLaser.Infrastructure.Persistence.Configurations;
using BookingCQBLaser.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Net;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

var builder = WebApplication.CreateBuilder(args);

// Check that this using directive is present to access UseNpgsql (often included implicitly, but good to ensure)
// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Google Calendar options
builder.Services.Configure<GoogleCalendarOptions>(
    builder.Configuration.GetSection("GoogleCalendarOptions"));

//Email sender
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("SmtpOptions"));

//PGateway
builder.Services.Configure<HotPayOptions>(builder.Configuration.GetSection("HotPayOptions"));

// Register repositories
builder.Services.AddScoped<IBookingRepository, BookingRepository>();



// Register external services
// GoogleCalendarService is a singleton: it holds no per-request state, and its Google credential
// should be resolved once per process rather than mutating GOOGLE_APPLICATION_CREDENTIALS per request.
builder.Services.AddSingleton<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IHotPayService, HotPayService>();

// The API sits behind a reverse proxy on the same Ubuntu host/Docker network, so trust
// X-Forwarded-For only when it comes from that local/private network - never from the open
// internet - otherwise HotPayIpWhitelistFilter's IP check is trivially spoofable.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Loopback, 8));
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
    options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
});

//Background Services
builder.Services.AddHostedService<ExpiredBookingCleanupService>();
builder.Services.AddScoped<HotPayIpWhitelistFilter>();


// Register application services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IAvailabilityCalculator, AvailabilityCalculator>();
builder.Services.AddScoped<IBookingService, BookingService>();

// Configure CORS for Angular development
builder.Services.AddCors(options =>
{
    options.AddPolicy("ProductionCorsPolicy", policy =>
    {
        policy.WithOrigins(
                "https://comboarena.netlify.app",
                "https://comboarena.pl",
                "https://www.comboarena.pl")
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply any pending EF Core migrations on startup, so the schema is never out of sync with
// what the running code expects (e.g. the overlap-exclusion constraint / indexes migration).
// Safe to run on every startup: Migrate() is a no-op once the database is already up to date.
using (var migrationScope = app.Services.CreateScope())
{
    var dbContext = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Must run before anything that reads the client IP (CORS, auth, the HotPay IP whitelist filter)
// so RemoteIpAddress is already resolved from the trusted proxy by the time they see it.
app.UseForwardedHeaders();

app.UseHttpsRedirection();

app.UseCors("ProductionCorsPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();
