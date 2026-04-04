using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Infrastructure.ExternalServices;
using BookingCQBLaser.Infrastructure.Persistence.Configurations;
using BookingCQBLaser.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure Google Calendar options
builder.Services.Configure<GoogleCalendarOptions>(
    builder.Configuration.GetSection("GoogleCalendarOptions"));

//Email sender
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("SmtpOptions"));

// Register repositories
builder.Services.AddScoped<IBookingRepository, BookingRepository>();

// Register external services
builder.Services.AddScoped<IGoogleCalendarService, GoogleCalendarService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Register application services
builder.Services.AddScoped<IBookingService, BookingService>();

// Configure CORS for Angular development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularApp", policy =>
    {
        policy.AllowAnyOrigin()  // Pozwala na KAŻDY adres (Netlify, localhost, telefon, itp.)
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

app.UseCors("AngularApp");

app.UseAuthorization();

app.MapControllers();

app.Run();
