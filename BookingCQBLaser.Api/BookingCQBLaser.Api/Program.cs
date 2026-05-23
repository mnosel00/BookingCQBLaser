using BookingCQBLaser.Api.Filters;
using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Infrastructure.BackgroundJobs;
using BookingCQBLaser.Infrastructure.ExternalServices;
using BookingCQBLaser.Infrastructure.ExternalServices.PGateway;
using BookingCQBLaser.Infrastructure.Persistence.Configurations;
using BookingCQBLaser.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.EntityFrameworkCore;

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
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IHotPayService, HotPayService>();

//Background Services
builder.Services.AddHostedService<ExpiredBookingCleanupService>();
builder.Services.AddScoped<HotPayIpWhitelistFilter>();


// Register application services
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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("ProductionCorsPolicy");

app.UseAuthorization();

app.MapControllers();

app.Run();
