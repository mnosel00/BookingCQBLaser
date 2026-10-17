using BookingCQBLaser.Application.DTOs;

namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Pure slot geometry: given the day's open/close boundaries and the periods already busy, decides
/// which start times to offer for a package. Isolated from <see cref="AvailabilityCalculator"/> (which
/// owns timezone, lead-time and I/O) so the packing strategy can evolve and be unit-tested on its own.
/// </summary>
public interface ISlotPacker
{
    /// <param name="dayStart">Arena open boundary for the day (grid-aligned, timezone-resolved).</param>
    /// <param name="dayEnd">Arena close boundary for the day.</param>
    /// <param name="busyPeriods">Already-occupied periods (local bookings + external blocks), unmerged.</param>
    /// <param name="packageBlockedDurationMinutes">How long the arena is occupied by this package.</param>
    /// <param name="clientGameDurationMinutes">Shorter, customer-facing end used only for the slot's display end.</param>
    IReadOnlyList<TimeSlotDto> Pack(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int packageBlockedDurationMinutes,
        int clientGameDurationMinutes);
}
