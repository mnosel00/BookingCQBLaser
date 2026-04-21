// ..\BookingCQBLaser.Application\Services\BookingService.cs
using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Entities;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Domain.ValueObject;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BookingCQBLaser.Application.Services;

public class BookingService : IBookingService
{
    private const int TurnaroundBufferMinutes = 30;
    private const int Group1TotalBlockedDurationMinutes = 90;  // S1, S2, Premium
    private const int Group2TotalBlockedDurationMinutes = 120; // Max, U1, U2, U3, Combat
    private static readonly TimeOnly ArenaOpenTime = new(8, 0);

    private readonly IBookingRepository _repository;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IEmailService _emailService;
    private readonly IHotPayService _hotpayService;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IBookingRepository repository,
        IGoogleCalendarService googleCalendarService,
        IEmailService emailService,
        IHotPayService hotpayService,
        ILogger<BookingService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _googleCalendarService = googleCalendarService ?? throw new ArgumentNullException(nameof(googleCalendarService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _hotpayService = hotpayService ?? throw new ArgumentNullException(nameof(hotpayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    private static TimeOnly GetLatestStartTime() => new(21, 0);

    private bool IsOnlineBookingAllowed(DateTimeOffset targetDate, DateTimeOffset nowInPoland)
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

    public async Task<IEnumerable<TimeSlotDto>> GetAvailableTimeSlotsAsync(
        DateTimeOffset date,
        CancellationToken cancellationToken = default)
    {
        var polandTimeZone = GetPolandTimeZone();
        var nowInPoland = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, polandTimeZone);
        var requestedDateInPoland = TimeZoneInfo.ConvertTimeFromUtc(date.UtcDateTime, polandTimeZone);

        if (!IsOnlineBookingAllowed(requestedDateInPoland, nowInPoland))
        {
            _logger.LogInformation("Requested date {Date} is not allowed by booking rules. Returning empty slots.", requestedDateInPoland.Date);
            return [];
        }

        var dayStartLocal = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            ArenaOpenTime.Hour, ArenaOpenTime.Minute, 0,
            date.Offset);

        var dayEndLocal = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            23, 0, 0,
            date.Offset);

        var latestStartLocal = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            GetLatestStartTime().Hour, GetLatestStartTime().Minute, 0,
            date.Offset);

        var dayStartUtc = dayStartLocal.ToUniversalTime();
        var dayEndUtc = dayEndLocal.ToUniversalTime();
        var latestStartUtc = latestStartLocal.ToUniversalTime();

        // Hard daily limit based on local bookings only.
        var localBookings = (await _repository.GetByDateRangeAsync(dayStartUtc, dayEndUtc, cancellationToken)).ToList();
        if (localBookings.Count >= 7)
        {
            _logger.LogInformation(
                "Day {Date} already has {Count} local bookings (limit: 7). Returning empty slots.",
                requestedDateInPoland.Date, localBookings.Count);
            return [];
        }

        // NEW: Strict Docking Mode
        bool isStrictDockingMode = localBookings.Count >= 3;

        var googleBusyPeriods = await _googleCalendarService.GetBusyPeriodsAsync(dayStartUtc, dayEndUtc, cancellationToken);

        var busyPeriods = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var booking in localBookings)
        {
            if (string.IsNullOrEmpty(booking.GoogleCalendarEventId))
            {
                busyPeriods.Add((booking.StartTime, booking.EndTime));
            }
        }

        busyPeriods.AddRange(googleBusyPeriods);

        // Clamp busy periods to [08:00, 23:00] and merge.
        var mergedBusyPeriods = MergeBusyPeriods(
            busyPeriods
                .Where(p => p.End > dayStartUtc && p.Start < dayEndUtc)
                .Select(p => (
                    Start: p.Start < dayStartUtc ? dayStartUtc : p.Start,
                    End: p.End > dayEndUtc ? dayEndUtc : p.End))
                .ToList());

        var gaps = BuildGaps(mergedBusyPeriods, dayStartUtc, dayEndUtc);
        var priorityGaps = gaps
            .Where(g => g.IsInternal && g.DurationMinutes <= 240 && IsGapFillable(g.DurationMinutes, false))
            .ToList();

        bool isPriorityMode = priorityGaps.Count > 0;

        var slots = new List<TimeSlotDto>();

        for (var currentSlotStartUtc = dayStartUtc;
             currentSlotStartUtc <= latestStartUtc;
             currentSlotStartUtc = currentSlotStartUtc.AddMinutes(30))
        {
            // Cannot start inside a busy period.
            if (mergedBusyPeriods.Any(p => currentSlotStartUtc >= p.Start && currentSlotStartUtc < p.End))
            {
                continue;
            }

            // Previous booking end (or day start).
            DateTimeOffset? closestPreviousBookingEnd = null;
            foreach (var period in mergedBusyPeriods)
            {
                if (period.End <= currentSlotStartUtc &&
                    (closestPreviousBookingEnd == null || period.End > closestPreviousBookingEnd.Value))
                {
                    closestPreviousBookingEnd = period.End;
                }
            }

            // Next booking start (or day end).
            DateTimeOffset? closestNextBookingStart = null;
            foreach (var period in mergedBusyPeriods)
            {
                if (period.Start > currentSlotStartUtc &&
                    (closestNextBookingStart == null || period.Start < closestNextBookingStart.Value))
                {
                    closestNextBookingStart = period.Start;
                }
            }

            // NEW: strict docking pre-check right after previous/next lookup
            bool docksAtStart = closestPreviousBookingEnd.HasValue && currentSlotStartUtc == closestPreviousBookingEnd.Value;
            bool docksAtEndFor90 = closestNextBookingStart.HasValue && currentSlotStartUtc.AddMinutes(Group1TotalBlockedDurationMinutes) == closestNextBookingStart.Value;
            bool docksAtEndFor120 = closestNextBookingStart.HasValue && currentSlotStartUtc.AddMinutes(Group2TotalBlockedDurationMinutes) == closestNextBookingStart.Value;

            if (isStrictDockingMode && !(docksAtStart || docksAtEndFor90 || docksAtEndFor120))
            {
                continue;
            }

            var previousBoundaryUtc = closestPreviousBookingEnd ?? dayStartUtc;
            bool isStartBoundary = closestPreviousBookingEnd == null;

            var nextBoundaryUtc = closestNextBookingStart ?? dayEndUtc;
            bool isEndBoundary = closestNextBookingStart == null;

            var gapBeforeMinutes = (int)(currentSlotStartUtc - previousBoundaryUtc).TotalMinutes;
            var spaceAheadMinutes = (int)(nextBoundaryUtc - currentSlotStartUtc).TotalMinutes;

            // Find the gap that contains current candidate.
            var containingGap = gaps.FirstOrDefault(g => currentSlotStartUtc >= g.Start && currentSlotStartUtc < g.End);
            if (containingGap.DurationMinutes == 0)
            {
                continue;
            }

            // Mode-based filtering:
            if (isPriorityMode)
            {
                // Must be inside one of the <=240 internal gaps.
                if (!(containingGap.IsInternal && containingGap.DurationMinutes <= 240))
                {
                    continue;
                }
            }
            else
            {
                // Normal mode: block candidates creating dead holes behind (except start-of-day boundary).
                if (!IsGapFillable(gapBeforeMinutes, isStartBoundary))
                {
                    continue;
                }
            }

            bool CanUseDuration(int durationMinutes)
            {
                if (spaceAheadMinutes < durationMinutes)
                {
                    return false;
                }

                var currentSlotEndUtc = currentSlotStartUtc.AddMinutes(durationMinutes);

                // Priority mode edge docking inside small gaps.
                if (isPriorityMode)
                {
                    bool touchesGapEdge = currentSlotStartUtc == containingGap.Start || currentSlotEndUtc == containingGap.End;
                    if (!touchesGapEdge)
                    {
                        return false;
                    }
                }

                // Strict docking rule per specific duration.
                if (isStrictDockingMode)
                {
                    bool docksForThisDuration =
                        docksAtStart ||
                        (durationMinutes == Group1TotalBlockedDurationMinutes && docksAtEndFor90) ||
                        (durationMinutes == Group2TotalBlockedDurationMinutes && docksAtEndFor120);

                    if (!docksForThisDuration)
                    {
                        return false;
                    }
                }

                var remainingAfterMinutes = spaceAheadMinutes - durationMinutes;
                return IsGapFillable(remainingAfterMinutes, isEndBoundary);
            }

            // Evaluate both independently.
            bool fits120 = CanUseDuration(Group2TotalBlockedDurationMinutes);
            bool fits90 = CanUseDuration(Group1TotalBlockedDurationMinutes);

            if (fits90 || fits120)
            {
                slots.Add(new TimeSlotDto(
                    currentSlotStartUtc.ToOffset(date.Offset),
                    fits90,
                    fits120));
            }
        }

        // Aggregate by start time and OR the package-permission flags.
        return slots
            .GroupBy(x => x.StartTime)
            .Select(g => new TimeSlotDto(
                g.Key,
                g.Any(x => x.Is90MinutePackageAllowed),
                g.Any(x => x.Is120MinutePackageAllowed)))
            .OrderBy(x => x.StartTime)
            .ToList();
    }

    public async Task<CreateBookingResponseDto> CreateBookingAsync(
        CreateBookingDto dto,
        CancellationToken cancellationToken = default)
    {
        var polandTimeZone = GetPolandTimeZone();
        var nowInPoland = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, polandTimeZone);
        var requestedDateInPoland = TimeZoneInfo.ConvertTimeFromUtc(dto.StartTime.UtcDateTime, polandTimeZone);

        if (!IsOnlineBookingAllowed(requestedDateInPoland, nowInPoland))
        {
            throw new InvalidOperationException("Rezerwacja online w tym dniu jest wyłączona, prosimy o kontakt telefoniczny lub SMS: 509 595 199");
        }

        var availableSlots = await GetAvailableTimeSlotsAsync(dto.StartTime, cancellationToken);
        var requestedTotalBlockedDuration = dto.Package.GetBaseDurationMinutes() + TurnaroundBufferMinutes;

        var isSlotAvailable = availableSlots.Any(slot =>
            slot.StartTime == dto.StartTime &&
            (requestedTotalBlockedDuration switch
            {
                Group1TotalBlockedDurationMinutes => slot.Is90MinutePackageAllowed,
                Group2TotalBlockedDurationMinutes => slot.Is120MinutePackageAllowed,
                _ => false
            }));

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

        var booking = new Booking(
            customerInfo,
            dto.ParticipantsCount,
            dto.Package,
            dto.StartTime.ToUniversalTime(),
            requestedTotalBlockedDuration);

        await _repository.AddAsync(booking, cancellationToken);
        _logger.LogInformation("Booking {BookingId} saved to database as Pending", booking.Id);

        int depositAmountP = 304;
        string paymentUrl = _hotpayService.GeneratePaymentUrl(booking, depositAmountP);

        return new CreateBookingResponseDto(booking.Id, paymentUrl);
    }

    public async Task ConfirmBookingPaymentAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _repository.GetByIdAsync(bookingId, cancellationToken);

        if (booking == null)
        {
            _logger.LogError("Booking with ID {Id} not found during payment confirmation.", bookingId);
            throw new KeyNotFoundException($"Booking with ID {bookingId} not found.");
        }

        if (booking.PaymentStatus == PaymentStatus.Paid)
        {
            _logger.LogInformation("Booking {Id} was already processed as paid.", booking.Id);
            return;
        }

        // 1. Update DB Status
        booking.MarkAsPaid();
        await _repository.UpdateAsync(booking, cancellationToken);
        _logger.LogInformation("Booking {Id} marked as paid.", booking.Id);

        // 2. Sync to Calendar
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
                "Failed to create Google Calendar event for paid booking {BookingId}. Booking saved but calendar sync failed.",
                booking.Id);
        }

        // 3. Send Email
        try
        {
            int packagePrice = booking.Package.GetPrice();
            int totalCost = packagePrice * booking.ParticipantsCount;
            int depositAmount = 300;
            int remainingBalance = Math.Max(0, totalCost - depositAmount);

            await _emailService.SendBookingConfirmationAsync(booking, totalCost, depositAmount, remainingBalance);
            _logger.LogInformation("Confirmation email sent to {Email} for booking {BookingId}", booking.Customer.Email, booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email to {Email} for paid booking {BookingId}.", booking.Customer.Email, booking.Id);
        }
    }

    private readonly record struct Gap(DateTimeOffset Start, DateTimeOffset End, bool IsInternal)
    {
        public int DurationMinutes => (int)(End - Start).TotalMinutes;
    }

    private static List<Gap> BuildGaps(
        List<(DateTimeOffset Start, DateTimeOffset End)> mergedBusyPeriods,
        DateTimeOffset dayStartUtc,
        DateTimeOffset dayEndUtc)
    {
        var gaps = new List<Gap>();

        if (mergedBusyPeriods.Count == 0)
        {
            gaps.Add(new Gap(dayStartUtc, dayEndUtc, false));
            return gaps;
        }

        var first = mergedBusyPeriods[0];
        if (dayStartUtc < first.Start)
        {
            gaps.Add(new Gap(dayStartUtc, first.Start, false));
        }

        for (int i = 0; i < mergedBusyPeriods.Count - 1; i++)
        {
            var left = mergedBusyPeriods[i];
            var right = mergedBusyPeriods[i + 1];

            if (left.End < right.Start)
            {
                gaps.Add(new Gap(left.End, right.Start, true));
            }
        }

        var last = mergedBusyPeriods[^1];
        if (last.End < dayEndUtc)
        {
            gaps.Add(new Gap(last.End, dayEndUtc, false));
        }

        return gaps.Where(g => g.DurationMinutes > 0).ToList();
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

    private static bool IsGapFillable(int minutes, bool isBoundaryGap = false)
    {
        if (isBoundaryGap) return true;
        if (minutes == 0) return true;
        if (minutes < 90) return false;
        if (minutes == 150) return false;
        return minutes % 30 == 0;
    }
}