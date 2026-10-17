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

        // Geometry (free windows + which starts to offer) lives in the pure packer.
        var availableSlots = _slotPacker.Pack(dayStart, dayEnd, busyPeriods, packageBlockedDuration, clientGameDuration);

        _logger.LogInformation(
            "Found {Count} available slots for {Date} with package {Package}",
            availableSlots.Count, date.Date, package);

        return availableSlots;
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
