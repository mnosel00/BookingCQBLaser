using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Application.Services;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BookingCQBLaser.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpGet("available-slots")]
    public async Task<IActionResult> GetAvailableSlots(
        [FromQuery] DateTimeOffset date,
        CancellationToken cancellationToken)
    {
        var slots = await _bookingService.GetAvailableTimeSlotsAsync(date, cancellationToken);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking(
        [FromBody] CreateBookingDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.CreateBookingAsync(dto, cancellationToken);
        return Ok(result);
    }
}