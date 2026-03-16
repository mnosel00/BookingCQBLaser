using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Domain.ValueObject;
using Microsoft.Extensions.Logging;

namespace BookingCQBLaser.Application.Services;

public class BookingService : IBookingService
{
    private const int TurnaroundBufferMinutes = 30;
    private const int SlotIntervalMinutes = 10;
    private static readonly TimeOnly ArenaOpenTime = new(8, 0);
    private static readonly TimeOnly LatestStartTime = new(22, 0);

    private readonly IBookingRepository _repository;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IBookingRepository repository,
        IGoogleCalendarService googleCalendarService,
        ILogger<BookingService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _googleCalendarService = googleCalendarService ?? throw new ArgumentNullException(nameof(googleCalendarService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<TimeSlotDto>> GetAvailableTimeSlotsAsync(
        DateTimeOffset date,
        PackageType package,
        CancellationToken cancellationToken = default)
    {
        var packageBaseDuration = package.GetBaseDurationMinutes();
        var totalDurationMinutes = packageBaseDuration + TurnaroundBufferMinutes;

        var dayStart = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            ArenaOpenTime.Hour, ArenaOpenTime.Minute, 0,
            date.Offset);

        var latestStart = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            LatestStartTime.Hour, LatestStartTime.Minute, 0,
            date.Offset);

        var dayEnd = dayStart.AddDays(1);

        var busyPeriods = await GetCombinedBusyPeriodsAsync(dayStart, dayEnd, cancellationToken);

        var availableSlots = new List<TimeSlotDto>();
        var currentSlotStart = dayStart;

        while (currentSlotStart <= latestStart)
        {
            var currentSlotEnd = currentSlotStart.AddMinutes(totalDurationMinutes);

            if (!OverlapsWithBusyPeriods(currentSlotStart, currentSlotEnd, busyPeriods))
            {
                var displaySlotEnd = currentSlotStart.AddMinutes(packageBaseDuration);
                availableSlots.Add(new TimeSlotDto(currentSlotStart, displaySlotEnd));
            }

            currentSlotStart = currentSlotStart.AddMinutes(SlotIntervalMinutes);
        }

        _logger.LogInformation(
            "Found {Count} available slots for {Date} with package {Package}",
            availableSlots.Count, date.Date, package);

        return availableSlots;
    }

    public async Task<Guid> CreateBookingAsync(
    CreateBookingDto dto,
    CancellationToken cancellationToken = default)
    {
        // Verify slot availability to prevent race conditions
        var availableSlots = await GetAvailableTimeSlotsAsync(dto.StartTime, dto.Package, cancellationToken);
        var isSlotAvailable = availableSlots.Any(slot => slot.StartTime == dto.StartTime);

        if (!isSlotAvailable)
        {
            _logger.LogWarning(
                "Attempted to book unavailable slot at {StartTime} for package {Package}",
                dto.StartTime, dto.Package);
            throw new InvalidOperationException($"The requested time slot at {dto.StartTime} is no longer available.");
        }

        var customerInfo = new CustomerInfo(
            dto.FirstName,
            dto.LastName,
            dto.Email,
            dto.Phone);

        var totalBlockedDurationMinutes = dto.Package.GetBaseDurationMinutes() + TurnaroundBufferMinutes;

        var booking = new Booking(
            customerInfo,
            dto.ParticipantsCount,
            dto.Package,
            dto.StartTime,
            totalBlockedDurationMinutes);

        await _repository.AddAsync(booking, cancellationToken);
        _logger.LogInformation("Booking {BookingId} saved to database", booking.Id);

        try
        {
            var googleEventId = await _googleCalendarService.CreateEventAsync(booking, cancellationToken);
            booking.UpdateGoogleCalendarEventId(googleEventId);
            await _repository.UpdateAsync(booking, cancellationToken);

            _logger.LogInformation(
                "Google Calendar event {EventId} created for booking {BookingId}",
                googleEventId, booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create Google Calendar event for booking {BookingId}. Booking saved but calendar sync failed.",
                booking.Id);
            // Booking is still valid, calendar sync can be retried later
        }

        return booking.Id;
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

        // Add local bookings as busy periods ONLY if they haven't been synced to Google Calendar
        // If they have a Google Event ID, let Google's FreeBusy response dictate if the time is actually blocked
        foreach (var booking in localBookings)
        {
            if (string.IsNullOrEmpty(booking.GoogleCalendarEventId))
            {
                busyPeriods.Add((booking.StartTime, booking.EndTime));
            }
        }

        // Add Google Calendar busy periods
        busyPeriods.AddRange(googleBusyPeriods);

        // Sort and merge overlapping periods for efficiency
        return MergeBusyPeriods(busyPeriods);
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> MergeBusyPeriods(
        List<(DateTimeOffset Start, DateTimeOffset End)> periods)
    {
        if (periods.Count == 0)
            return periods;

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

    private static bool OverlapsWithBusyPeriods(
        DateTimeOffset slotStart,
        DateTimeOffset slotEnd,
        List<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods)
    {
        foreach (var (busyStart, busyEnd) in busyPeriods)
        {
            // Check if slot overlaps with busy period
            if (slotStart < busyEnd && slotEnd > busyStart)
            {
                return true;
            }
        }

        return false;
    }
}