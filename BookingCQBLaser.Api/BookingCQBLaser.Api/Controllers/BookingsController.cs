using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
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
        [FromQuery] PackageType package,
        CancellationToken cancellationToken)
    {
        var slots = await _bookingService.GetAvailableTimeSlotsAsync(date, package, cancellationToken);
        return Ok(slots);
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking(
        [FromBody] CreateBookingDto dto,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _bookingService.CreateBookingAsync(dto, cancellationToken);
            // Correctly return the DTO object containing BookingId and PaymentUrl
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Slot no longer available - either the pre-check found it taken, or a concurrent
            // request won the race and the database's overlap constraint rejected this one.
            return Conflict(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{id}/status")]
    public async Task<IActionResult> GetBookingStatus(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var status = await _bookingService.GetBookingStatusAsync(id, cancellationToken);
            return Ok(status);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}