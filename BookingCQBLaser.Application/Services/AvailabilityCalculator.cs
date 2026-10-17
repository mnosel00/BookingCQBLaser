using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace BookingCQBLaser.Application.Services;

public class AvailabilityCalculator : IAvailabilityCalculator
{
    private readonly IBookingRepository _repository;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly ISlotPacker _slotPacker;
    private readonly AvailabilityOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AvailabilityCalculator> _logger;

    public AvailabilityCalculator(
        IBookingRepository repository,
        IGoogleCalendarService googleCalendarService,
        ISlotPacker slotPacker,
        AvailabilityOptions options,
        ILogger<AvailabilityCalculator> logger,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _googleCalendarService = googleCalendarService ?? throw new ArgumentNullException(nameof(googleCalendarService));
        _slotPacker = slotPacker ?? throw new ArgumentNullException(nameof(slotPacker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
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

    private bool IsOnlineBookingAllowedCore(DateTimeOffset targetDate, DateTimeOffset nowInPoland)
    {
        if (targetDate.Date < nowInPoland.Date) return false;

        if (targetDate.DayOfWeek == DayOfWeek.Saturday)
        {
            var deadline = targetDate.Date.AddDays(-1).AddHours(_options.WeekendCutoffHour); // Friday 22:00
            return nowInPoland <= deadline;
        }
        if (targetDate.DayOfWeek == DayOfWeek.Sunday)
        {
            var deadline = targetDate.Date.AddDays(-1).AddHours(_options.WeekendCutoffHour); // Saturday 22:00
            return nowInPoland <= deadline;
        }

        // Monday-Friday (3 calendar days rule)
        return targetDate.Date >= nowInPoland.Date.AddDays(_options.WeekdayLeadTimeDays);
    }

    private TimeOnly GetArenaCloseTime(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday
            ? _options.SundayCloseTime
            : _options.WeekdayCloseTime;
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

        var dayStart = BuildPolandBoundary(polandTimeZone, dateOnlyInPoland, _options.ArenaOpenTime);
        var dayEnd = BuildPolandBoundary(polandTimeZone, dateOnlyInPoland, arenaCloseTime);

        // Convert boundaries to UTC for DB and Google Calendar queries
        var busyPeriods = await GetCombinedBusyPeriodsAsync(
            dayStart.ToUniversalTime(),
            dayEnd.ToUniversalTime(),
            cancellationToken);

        // The packing rule depends on the day of week. Timezone/DST-aware boundary and anchor instants
        // are built here; the pure packer only sees instants.
        var availableSlots = requestedDateInPoland.DayOfWeek switch
        {
            DayOfWeek.Sunday => PackSunday(polandTimeZone, dateOnlyInPoland, dayStart, dayEnd, busyPeriods, packageBlockedDuration, clientGameDuration),
            DayOfWeek.Saturday => _slotPacker.PackTileable(dayStart, dayEnd, busyPeriods, packageBlockedDuration, clientGameDuration),
            _ => PackWeekday(polandTimeZone, dateOnlyInPoland, dayStart, dayEnd, busyPeriods, packageBlockedDuration, clientGameDuration),
        };

        _logger.LogInformation(
            "Found {Count} available slots for {Date} ({Day}) with package {Package}",
            availableSlots.Count, date.Date, requestedDateInPoland.DayOfWeek, package);

        return availableSlots;
    }

    /// <summary>
    /// Sunday: the first group of the day may start at any grid time between open and
    /// <see cref="AvailabilityOptions.SundayFirstStartLatest"/>; once any group exists, later groups
    /// must stick flush to an existing booking. Close is earlier than on other days.
    /// </summary>
    private IReadOnlyList<TimeSlotDto> PackSunday(
        TimeZoneInfo tz, DateTime date, DateTimeOffset dayStart, DateTimeOffset dayEnd,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int blocked, int client)
    {
        var anchors = BuildAnchorGrid(tz, date, _options.ArenaOpenTime, _options.SundayFirstStartLatest);
        var hasGroup = busyPeriods.Any(b => b.Start >= dayStart);
        return _slotPacker.PackAnchoredThenFlush(dayStart, dayEnd, anchors, hasGroup, busyPeriods, blocked, client);
    }

    /// <summary>
    /// Monday–Friday: a morning session (tileable starts up to <see cref="AvailabilityOptions.MorningLatestStart"/>,
    /// whose blocks may run into the otherwise-dead afternoon) and an evening session that anchors the
    /// first group to fixed starts then sticks flush. Nothing is bookable between the two sessions.
    /// </summary>
    private IReadOnlyList<TimeSlotDto> PackWeekday(
        TimeZoneInfo tz, DateTime date, DateTimeOffset dayStart, DateTimeOffset dayEnd,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods,
        int blocked, int client)
    {
        var morningLatestStart = BuildPolandBoundary(tz, date, _options.MorningLatestStart);
        var eveningFloor = BuildPolandBoundary(tz, date, _options.EveningWindowStart);

        var morning = _slotPacker
            .PackTileable(dayStart, dayEnd, busyPeriods, blocked, client)
            .Where(s => s.StartTime <= morningLatestStart);

        var eveningAnchors = BuildAnchorGrid(tz, date, _options.EveningWindowStart, _options.EveningWindowEnd);
        eveningAnchors.Add(BuildPolandBoundary(tz, date, _options.EveningExtraAnchor));

        // A morning booking never pushes the evening into flush mode - only a booking that starts in
        // the evening session does.
        var eveningHasGroup = busyPeriods.Any(b => b.Start >= eveningFloor);
        var evening = _slotPacker.PackAnchoredThenFlush(eveningFloor, dayEnd, eveningAnchors, eveningHasGroup, busyPeriods, blocked, client);

        return morning.Concat(evening).OrderBy(s => s.StartTime).ToList();
    }

    /// <summary>Grid-aligned start instants from <paramref name="from"/> to <paramref name="to"/> inclusive, DST-safe.</summary>
    private List<DateTimeOffset> BuildAnchorGrid(TimeZoneInfo tz, DateTime date, TimeOnly from, TimeOnly to)
    {
        var anchors = new List<DateTimeOffset>();
        for (var t = from; t <= to; t = t.AddMinutes(_options.SlotIntervalMinutes))
        {
            anchors.Add(BuildPolandBoundary(tz, date, t));
        }
        return anchors;
    }

    /// <summary>
    /// Gathers everything that occupies arena time in the window — local bookings that aren't
    /// already mirrored into Google Calendar, plus Google Calendar busy blocks — as a raw, unmerged
    /// list. Overlap merging and free-window computation are the packer's job.
    /// </summary>
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
        return busyPeriods;
    }
}
