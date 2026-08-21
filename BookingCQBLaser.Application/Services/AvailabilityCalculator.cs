using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BookingCQBLaser.Application.Services;

public class AvailabilityCalculator : IAvailabilityCalculator
{
    private const int SlotIntervalMinutes = 30;

    private static readonly TimeOnly ArenaOpenTime = new(9, 0);

    private readonly IBookingRepository _repository;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AvailabilityCalculator> _logger;

    public AvailabilityCalculator(
        IBookingRepository repository,
        IGoogleCalendarService googleCalendarService,
        ILogger<AvailabilityCalculator> logger,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _googleCalendarService = googleCalendarService ?? throw new ArgumentNullException(nameof(googleCalendarService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    private static TimeZoneInfo GetPolandTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback for Windows instances without IANA time zone support
            return TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");
        }
    }

    public bool IsOnlineBookingAllowed(DateTimeOffset requestedStartTime)
    {
        var polandTimeZone = GetPolandTimeZone();
        var nowInPoland = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, polandTimeZone);
        var requestedDateInPoland = TimeZoneInfo.ConvertTimeFromUtc(requestedStartTime.UtcDateTime, polandTimeZone);
        return IsOnlineBookingAllowedCore(requestedDateInPoland, nowInPoland);
    }

    private static bool IsOnlineBookingAllowedCore(DateTimeOffset targetDate, DateTimeOffset nowInPoland)
    {
        if (targetDate.Date < nowInPoland.Date) return false;

        if (targetDate.DayOfWeek == DayOfWeek.Saturday)
        {
            var deadline = targetDate.Date.AddDays(-1).AddHours(22); // Friday 22:00
            return nowInPoland <= deadline;
        }
        if (targetDate.DayOfWeek == DayOfWeek.Sunday)
        {
            var deadline = targetDate.Date.AddDays(-1).AddHours(22); // Saturday 22:00
            return nowInPoland <= deadline;
        }

        // Monday-Friday (3 calendar days rule)
        return targetDate.Date >= nowInPoland.Date.AddDays(3);
    }

    private static TimeOnly GetArenaCloseTime(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday
            ? new TimeOnly(16, 0)
            : new TimeOnly(23, 0);
    }

    /// <summary>
    /// Builds an absolute instant for a wall-clock time on a given date, using the UTC offset
    /// in effect at that specific instant rather than at midnight — a date's midnight offset can
    /// differ from its business-hours offset on DST transition days.
    /// </summary>
    private static DateTimeOffset BuildPolandBoundary(TimeZoneInfo polandTimeZone, DateTime dateOnly, TimeOnly time)
    {
        var localDateTime = dateOnly.Add(time.ToTimeSpan());
        var offset = polandTimeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset);
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailableTimeSlotsAsync(
         DateTimeOffset date,
         PackageType package,
         CancellationToken cancellationToken = default)
    {
        var polandTimeZone = GetPolandTimeZone();
        var nowInPoland = TimeZoneInfo.ConvertTimeFromUtc(_timeProvider.GetUtcNow().UtcDateTime, polandTimeZone);

        var requestedDateInPoland = TimeZoneInfo.ConvertTimeFromUtc(date.UtcDateTime, polandTimeZone);

        if (!IsOnlineBookingAllowedCore(requestedDateInPoland, nowInPoland))
        {
            _logger.LogInformation("Requested date {Date} is not allowed by booking rules. Returning empty slots.", requestedDateInPoland.Date);
            return new List<TimeSlotDto>();
        }

        var packageBlockedDuration = package.GetBaseDurationMinutes();
        var clientGameDuration = package.GetClientGameDurationMinutes();

        var dateOnlyInPoland = requestedDateInPoland.Date;
        var arenaCloseTime = GetArenaCloseTime(requestedDateInPoland.DayOfWeek);

        var dayStart = BuildPolandBoundary(polandTimeZone, dateOnlyInPoland, ArenaOpenTime);
        var dayEnd = BuildPolandBoundary(polandTimeZone, dateOnlyInPoland, arenaCloseTime);

        // Convert boundaries to UTC for DB and Google Calendar queries
        var busyPeriods = await GetCombinedBusyPeriodsAsync(
            dayStart.ToUniversalTime(),
            dayEnd.ToUniversalTime(),
            cancellationToken);

        // Get free windows between busy periods
        var freeWindows = GetFreeWindows(dayStart, dayEnd, busyPeriods);

        // The first booking of an empty day may start anywhere; once anything is on the
        // calendar (a booking or an external Google Calendar block), every later booking must
        // sit flush against an existing busy period or the day's open/close boundary — never
        // floating with wasted gaps on both sides.
        var isFirstBookingOfDay = busyPeriods.Count == 0;

        var availableSlots = new List<TimeSlotDto>();
        foreach (var (windowStart, windowEnd) in freeWindows)
        {
            var windowSlots = isFirstBookingOfDay
                ? GenerateDenseSlotsForWindow(windowStart, windowEnd, packageBlockedDuration, clientGameDuration)
                : GenerateEdgeSlotsForWindow(windowStart, windowEnd, packageBlockedDuration, clientGameDuration);
            availableSlots.AddRange(windowSlots);
        }

        availableSlots = availableSlots.OrderBy(s => s.StartTime).ToList();

        _logger.LogInformation(
            "Found {Count} available slots for {Date} with package {Package} across {WindowCount} free windows ({Mode} mode)",
            availableSlots.Count, date.Date, package, freeWindows.Count, isFirstBookingOfDay ? "free-choice" : "stick-to-edges");

        return availableSlots;
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
    /// interval-aligned start time that leaves enough room for the package's actual blocked
    /// duration.
    /// </summary>
    private static List<TimeSlotDto> GenerateDenseSlotsForWindow(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int packageBlockedDuration,
        int clientGameDuration)
    {
        var slots = new List<TimeSlotDto>();
        var latestSlotStart = windowEnd.AddMinutes(-packageBlockedDuration);

        for (var slotStart = windowStart; slotStart <= latestSlotStart; slotStart = slotStart.AddMinutes(SlotIntervalMinutes))
        {
            var maxAvailableDuration = (int)(windowEnd - slotStart).TotalMinutes;
            var displaySlotEnd = slotStart.AddMinutes(clientGameDuration);
            slots.Add(new TimeSlotDto(slotStart, displaySlotEnd, maxAvailableDuration));
        }

        return slots;
    }

    /// <summary>
    /// Stick-to-edges mode: used once anything is already on the calendar that day. A free
    /// window only offers a start flush against its own start, or a start that makes the booking
    /// end flush against the window's end — never a start floating in the middle, which would
    /// strand unusable time on both sides.
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

    private async Task<List<(DateTimeOffset Start, DateTimeOffset End)>> GetCombinedBusyPeriodsAsync(
        DateTimeOffset dayStart,
        DateTimeOffset dayEnd,
        CancellationToken cancellationToken)
    {
        var localBookingsTask = _repository.GetByDateRangeAsync(dayStart, dayEnd, cancellationToken);
        var googleBusyPeriodsTask = _googleCalendarService.GetBusyPeriodsAsync(dayStart, dayEnd, cancellationToken);

        await Task.WhenAll(localBookingsTask, googleBusyPeriodsTask);

        var localBookings = await localBookingsTask;
        var googleBusyPeriods = await googleBusyPeriodsTask;

        var busyPeriods = new List<(DateTimeOffset Start, DateTimeOffset End)>();

        foreach (var booking in localBookings)
        {
            if (string.IsNullOrEmpty(booking.GoogleCalendarEventId))
            {
                busyPeriods.Add((booking.StartTime, booking.EndTime));
            }
        }

        busyPeriods.AddRange(googleBusyPeriods);
        return MergeBusyPeriods(busyPeriods);
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> MergeBusyPeriods(
        List<(DateTimeOffset Start, DateTimeOffset End)> periods)
    {
        if (periods.Count == 0) return periods;

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
