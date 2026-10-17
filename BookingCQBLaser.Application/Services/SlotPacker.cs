using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;

namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Waste-minimising packer. For each free window it offers every grid-aligned start whose leftover
/// time — on both sides of the booking — can itself be fully filled by back-to-back bookings, so the
/// day stays perfectly tileable while the customer keeps the widest choice that doesn't strand arena
/// time. When a window can't be filled exactly (e.g. an odd 150-minute gap), it still offers the
/// placements that strand the least.
/// </summary>
/// <remarks>
/// The arena books on a fixed grid and every package occupies a whole multiple of it (90 or 120 min),
/// so whether a leftover gap is fully fillable is the classic coin-problem: a gap is fillable iff it
/// is a sum of package durations. For {90,120} the only unfillable sizes at or above one package are
/// 30, 60 and 150 minutes — those are exactly the leftovers this packer avoids creating.
/// </remarks>
public sealed class SlotPacker : ISlotPacker
{
    private readonly AvailabilityOptions _options;

    // The distinct arena-occupancy durations any package can take — the "coins" for deciding whether
    // a leftover gap can be filled exactly. Derived from the package table so it stays correct if the
    // packages change.
    private static readonly int[] BlockedDurationsMinutes =
        Enum.GetValues<PackageType>().Select(p => p.GetBaseDurationMinutes()).Distinct().OrderBy(d => d).ToArray();

    public SlotPacker(AvailabilityOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IReadOnlyList<TimeSlotDto> Pack(
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
            slots.AddRange(PackWindow(windowStart, windowEnd, packageBlockedDurationMinutes, clientGameDurationMinutes));
        }

        return slots.OrderBy(s => s.StartTime).ToList();
    }

    /// <summary>
    /// Offers, for one free window, the grid-aligned starts that strand the least arena time. On a
    /// window that tiles exactly those are every start whose both leftovers are fully fillable (waste
    /// zero); on a window that can't tile exactly, the least-wasteful starts are offered rather than
    /// none, so an awkward gap still yields a bookable slot.
    /// </summary>
    private List<TimeSlotDto> PackWindow(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int packageBlockedDuration,
        int clientGameDuration)
    {
        var interval = _options.SlotIntervalMinutes;
        var windowMinutes = (int)(windowEnd - windowStart).TotalMinutes;

        if (windowMinutes < packageBlockedDuration)
        {
            return new List<TimeSlotDto>();
        }

        // Every grid-aligned start that still leaves room for the package's blocked duration, with
        // the arena time it would strand (unfillable leftover before + after the booking).
        var candidates = new List<(DateTimeOffset Start, int Waste)>();
        for (var start = windowStart; start.AddMinutes(packageBlockedDuration) <= windowEnd; start = start.AddMinutes(interval))
        {
            var before = (int)(start - windowStart).TotalMinutes;
            var after = (int)(windowEnd - start).TotalMinutes - packageBlockedDuration;
            candidates.Add((start, StrandedMinutes(before) + StrandedMinutes(after)));
        }

        var minWaste = candidates.Min(c => c.Waste);

        var slots = new List<TimeSlotDto>();
        foreach (var (start, waste) in candidates)
        {
            if (waste != minWaste) continue;

            var maxAvailableDuration = (int)(windowEnd - start).TotalMinutes;
            slots.Add(new TimeSlotDto(start, start.AddMinutes(clientGameDuration), maxAvailableDuration));
        }

        return slots;
    }

    /// <summary>
    /// Arena minutes that would be stranded by a leftover gap of <paramref name="gapMinutes"/> — the
    /// gap minus the most it can be filled with back-to-back bookings. Zero when the gap tiles exactly
    /// (including an empty gap). Solved as the coin problem over the package durations.
    /// </summary>
    private int StrandedMinutes(int gapMinutes)
    {
        if (gapMinutes <= 0) return 0;

        var interval = _options.SlotIntervalMinutes;

        // Anything below the grid can never be booked, so it is stranded outright.
        var gapUnits = gapMinutes / interval;
        var subGridRemainder = gapMinutes - gapUnits * interval;

        // Package durations in grid units (they are whole multiples of the grid).
        var coins = BlockedDurationsMinutes
            .Where(d => d % interval == 0)
            .Select(d => d / interval)
            .Where(u => u > 0)
            .Distinct()
            .ToArray();

        // reachable[x] == can x grid-units be formed exactly by summing package durations.
        var reachable = new bool[gapUnits + 1];
        reachable[0] = true;
        for (var x = 1; x <= gapUnits; x++)
        {
            foreach (var coin in coins)
            {
                if (x - coin >= 0 && reachable[x - coin])
                {
                    reachable[x] = true;
                    break;
                }
            }
        }

        var largestFillableUnits = 0;
        for (var x = gapUnits; x >= 0; x--)
        {
            if (reachable[x]) { largestFillableUnits = x; break; }
        }

        return subGridRemainder + (gapUnits - largestFillableUnits) * interval;
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
