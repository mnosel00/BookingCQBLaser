using BookingCQBLaser.Application.DTOs;

namespace BookingCQBLaser.Application.Services;

/// <summary>
/// Default packer, preserving the original behaviour: the first booking of an empty day may start
/// anywhere on the grid (free-choice); once anything is on the calendar, later bookings must sit
/// flush against an existing busy period or a day boundary (stick-to-edges), never floating.
/// </summary>
public sealed class SlotPacker : ISlotPacker
{
    private readonly AvailabilityOptions _options;

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

        // The first booking of an empty day may start anywhere; once anything is on the calendar
        // (a booking or an external block), every later booking must sit flush against an existing
        // busy period or the day's open/close boundary — never floating with wasted gaps on both sides.
        var isFirstBookingOfDay = merged.Count == 0;

        var slots = new List<TimeSlotDto>();
        foreach (var (windowStart, windowEnd) in freeWindows)
        {
            var windowSlots = isFirstBookingOfDay
                ? GenerateDenseSlotsForWindow(windowStart, windowEnd, packageBlockedDurationMinutes, clientGameDurationMinutes)
                : GenerateEdgeSlotsForWindow(windowStart, windowEnd, packageBlockedDurationMinutes, clientGameDurationMinutes);
            slots.AddRange(windowSlots);
        }

        return slots.OrderBy(s => s.StartTime).ToList();
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

    /// <summary>
    /// Free-choice mode: used only for the very first booking of an empty day. Offers every
    /// interval-aligned start time that leaves enough room for the package's actual blocked duration.
    /// </summary>
    private List<TimeSlotDto> GenerateDenseSlotsForWindow(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int packageBlockedDuration,
        int clientGameDuration)
    {
        var slots = new List<TimeSlotDto>();
        var latestSlotStart = windowEnd.AddMinutes(-packageBlockedDuration);

        for (var slotStart = windowStart; slotStart <= latestSlotStart; slotStart = slotStart.AddMinutes(_options.SlotIntervalMinutes))
        {
            var maxAvailableDuration = (int)(windowEnd - slotStart).TotalMinutes;
            var displaySlotEnd = slotStart.AddMinutes(clientGameDuration);
            slots.Add(new TimeSlotDto(slotStart, displaySlotEnd, maxAvailableDuration));
        }

        return slots;
    }

    /// <summary>
    /// Stick-to-edges mode: used once anything is already on the calendar that day. A free window
    /// only offers a start flush against its own start, or a start that makes the booking end flush
    /// against the window's end — never a start floating in the middle, which would strand unusable
    /// time on both sides.
    /// </summary>
    private static List<TimeSlotDto> GenerateEdgeSlotsForWindow(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int packageBlockedDuration,
        int clientGameDuration)
    {
        var slots = new List<TimeSlotDto>();
        var windowDurationMinutes = (int)(windowEnd - windowStart).TotalMinutes;

        if (windowDurationMinutes < packageBlockedDuration)
        {
            return slots;
        }

        var flushStart = windowStart;
        slots.Add(new TimeSlotDto(flushStart, flushStart.AddMinutes(clientGameDuration), windowDurationMinutes));

        var flushEndStart = windowEnd.AddMinutes(-packageBlockedDuration);
        if (flushEndStart != flushStart)
        {
            var maxAvailableDuration = (int)(windowEnd - flushEndStart).TotalMinutes;
            slots.Add(new TimeSlotDto(flushEndStart, flushEndStart.AddMinutes(clientGameDuration), maxAvailableDuration));
        }

        return slots;
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
