using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Provides the two packing strategies the arena uses.
/// <para><see cref="PackTileable"/> offers every grid-aligned start that fits, except ones that would
/// leave an unsellable dead break next to an existing booking: a booking may sit flush against another,
/// or leave a gap big enough to still sell a game, but never a small gap (shorter than the shortest
/// game) between it and a neighbour. Gaps against the day's open/close don't count.</para>
/// <para><see cref="PackAnchoredThenFlush"/> offers a fixed set of anchor starts while the region is
/// empty, then flush-only starts once a group exists, so groups chain with no empty window.</para>
/// </summary>
public sealed class SlotPacker : ISlotPacker
{
    private readonly AvailabilityOptions _options;

    // The shortest game any package books. A gap smaller than this can never be sold, so the packer
    // won't leave one between two bookings. Derived from the package table.
    private static readonly int MinGameMinutes =
        Enum.GetValues<PackageType>().Select(p => p.GetBaseDurationMinutes()).Min();

    public SlotPacker(AvailabilityOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<TimeSlotDto> PackTileable(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int packageBlockedDurationMinutes,
        int clientGameDurationMinutes)
    {
        var merged = MergeBusyPeriods(busyPeriods);
        var freeWindows = GetFreeWindows(dayStart, dayEnd, merged);

        var slots = new List<TimeSlotDto>();
        foreach (var (windowStart, windowEnd) in freeWindows)
        {
            // A window edge that is a day boundary (open/close) allows any gap; an edge that is an
            // existing booking must not be left with a small, unsellable break.
            var leftBoundedByBooking = windowStart != dayStart;
            var rightBoundedByBooking = windowEnd != dayEnd;
            slots.AddRange(PackWindow(windowStart, windowEnd, packageBlockedDurationMinutes, clientGameDurationMinutes,
                leftBoundedByBooking, rightBoundedByBooking));
        }

        return slots.OrderBy(s => s.StartTime).ToList();
    }

    public IReadOnlyList<TimeSlotDto> PackAnchoredThenFlush(
        DateTimeOffset regionFloor,
        DateTimeOffset regionEnd,
        IReadOnlyList<DateTimeOffset> anchorStarts,
        bool regionHasGroup,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int packageBlockedDurationMinutes,
        int clientGameDurationMinutes)
    {
        var merged = MergeBusyPeriods(busyPeriods);

        IEnumerable<DateTimeOffset> candidates;
        if (!regionHasGroup)
        {
            // First group of the region: free choice among the allowed anchor starts.
            candidates = anchorStarts;
        }
        else
        {
            // Every later group must stick flush to an existing booking - start exactly where one
            // ends, or end exactly where one begins - so groups chain with no empty window between them.
            var flush = new List<DateTimeOffset>();
            foreach (var (start, end) in merged)
            {
                flush.Add(end);                                       // flush against the end of a booking
                flush.Add(start.AddMinutes(-packageBlockedDurationMinutes)); // flush against the start of a booking
            }
            candidates = flush;
        }

        var slots = new List<TimeSlotDto>();
        var seen = new HashSet<DateTimeOffset>();
        foreach (var start in candidates.OrderBy(s => s))
        {
            var end = start.AddMinutes(packageBlockedDurationMinutes);

            if (start < regionFloor) continue;        // never before the region floor (e.g. 17:30)
            if (end > regionEnd) continue;            // must finish by the arena close
            if (OverlapsAny(start, end, merged)) continue;
            if (!seen.Add(start)) continue;

            var maxAvailableDuration = (int)(NextBoundaryAfter(start, merged, regionEnd) - start).TotalMinutes;
            slots.Add(new TimeSlotDto(start, start.AddMinutes(clientGameDurationMinutes), maxAvailableDuration));
        }

        return slots.OrderBy(s => s.StartTime).ToList();
    }

    private static bool OverlapsAny(
        DateTimeOffset start,
        DateTimeOffset end,
        List<(DateTimeOffset Start, DateTimeOffset End)> merged)
        => merged.Any(b => start < b.End && b.Start < end);

    /// <summary>The next busy-period start strictly after <paramref name="start"/>, else the region end.</summary>
    private static DateTimeOffset NextBoundaryAfter(
        DateTimeOffset start,
        List<(DateTimeOffset Start, DateTimeOffset End)> merged,
        DateTimeOffset regionEnd)
    {
        var next = regionEnd;
        foreach (var (busyStart, _) in merged)
        {
            if (busyStart > start && busyStart < next) next = busyStart;
        }
        return next;
    }

    /// <summary>
    /// Offers every grid-aligned start that fits, except those that would strand an unsellable break
    /// (shorter than the shortest game) between this booking and an adjacent existing booking. A gap
    /// against a day boundary (open/close) is always allowed; a gap next to a booking must be zero
    /// (flush) or at least one game long.
    /// </summary>
    private List<TimeSlotDto> PackWindow(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int packageBlockedDuration,
        int clientGameDuration,
        bool leftBoundedByBooking,
        bool rightBoundedByBooking)
    {
        var interval = _options.SlotIntervalMinutes;
        var slots = new List<TimeSlotDto>();

        for (var start = windowStart; start.AddMinutes(packageBlockedDuration) <= windowEnd; start = start.AddMinutes(interval))
        {
            var leftGap = (int)(start - windowStart).TotalMinutes;
            var rightGap = (int)(windowEnd - start).TotalMinutes - packageBlockedDuration;

            // Never leave a small dead break next to an existing booking (gaps at the day's edges are fine).
            if (leftBoundedByBooking && leftGap != 0 && leftGap < MinGameMinutes) continue;
            if (rightBoundedByBooking && rightGap != 0 && rightGap < MinGameMinutes) continue;

            var maxAvailableDuration = (int)(windowEnd - start).TotalMinutes;
            slots.Add(new TimeSlotDto(start, start.AddMinutes(clientGameDuration), maxAvailableDuration));
        }

        return slots;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> GetFreeWindows(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        List<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods)
    {
        var freeWindows = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        if (busyPeriods.Count == 0)
        {
            freeWindows.Add((dayStart, dayEnd));
            return freeWindows;
        }

        // Window before first busy period
        if (dayStart < busyPeriods[0].Start)
        {
            freeWindows.Add((dayStart, busyPeriods[0].Start));
        }

        // Windows between consecutive busy periods
        for (int i = 0; i < busyPeriods.Count - 1; i++)
        {
            var windowStart = busyPeriods[i].End;
            var windowEnd = busyPeriods[i + 1].Start;

            if (windowStart < windowEnd)
            {
                freeWindows.Add((windowStart, windowEnd));
            }
        }

        // Window after last busy period
        if (busyPeriods[^1].End < dayEnd)
        {
            freeWindows.Add((busyPeriods[^1].End, dayEnd));
        }

        return freeWindows;
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> MergeBusyPeriods(
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> periods)
    {
        if (periods.Count == 0) return new List<(DateTimeOffset Start, DateTimeOffset End)>();

        var sorted = periods.OrderBy(p => p.Start).ToList();
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)> { sorted[0] };

        foreach (var current in sorted.Skip(1))
        {
            var last = merged[^1];

            if (current.Start <= last.End)
            {
                merged[^1] = (last.Start, current.End > last.End ? current.End : last.End);
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
