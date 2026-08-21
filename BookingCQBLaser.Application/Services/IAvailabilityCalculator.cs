using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Computes bookable arena time slots. Hides Poland-timezone conversion, the online-booking
/// lead-time rule, arena open/close hours (including DST-safe boundary math), and the
/// free-choice-vs-stick-to-edges slot algorithm behind two calls.
/// </summary>
public interface IAvailabilityCalculator
{
    Task<IEnumerable<TimeSlotDto>> GetAvailableTimeSlotsAsync(
        DateTimeOffset date, PackageType package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether online booking is open yet for the given start time, per the lead-time rule
    /// (3 calendar days Mon-Fri, Friday/Saturday 22:00 cutoff for Sat/Sun). Exposed separately
    /// from <see cref="GetAvailableTimeSlotsAsync"/> so callers can give a specific "booking
    /// window closed" message instead of a generic "slot unavailable" one.
    /// </summary>
    bool IsOnlineBookingAllowed(DateTimeOffset requestedStartTime);
}
