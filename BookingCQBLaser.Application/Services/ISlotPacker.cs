using BookingCQBLaser.Application.DTOs;

namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Pure slot geometry: given a region's bounds and the periods already busy, decides which start
/// times to offer for a package. Isolated from <see cref="AvailabilityCalculator"/> (which owns
/// timezone, lead-time, day-of-week policy and I/O) so the strategies can be unit-tested on their own.
/// </summary>
public interface ISlotPacker
{
    /// <summary>
    /// Offers every grid-aligned start that physically fits in a free window, so the customer can book
    /// any package at any available time even if it leaves an idle gap. Used for Saturday and weekday
    /// mornings.
    /// </summary>
    IReadOnlyList<TimeSlotDto> PackTileable(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int packageBlockedDurationMinutes,
        int clientGameDurationMinutes);

    /// <summary>
    /// Anchored-then-flush packing: while the region is empty the first group may start at one of the
    /// <paramref name="anchorStarts"/>; once a group exists (<paramref name="regionHasGroup"/>) only
    /// starts flush against an existing booking are offered, so groups chain with no empty window.
    /// Used for Sunday (whole day) and weekday evenings.
    /// </summary>
    /// <param name="regionFloor">Earliest allowed start in the region (anchors and flush starts obey it).</param>
    /// <param name="regionEnd">Latest instant a booking may end (the arena close for the region).</param>
    IReadOnlyList<TimeSlotDto> PackAnchoredThenFlush(
        DateTimeOffset regionFloor,
        DateTimeOffset regionEnd,
        IReadOnlyList<DateTimeOffset> anchorStarts,
        bool regionHasGroup,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int packageBlockedDurationMinutes,
        int clientGameDurationMinutes);
}
