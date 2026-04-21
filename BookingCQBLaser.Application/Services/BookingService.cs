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
    private static readonly int[] AnchorGridHours = [8, 10, 12, 14, 16, 18, 20];

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
            8, 0, 0,
            date.Offset);

        var dayCloseLocal = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            23, 0, 0,
            date.Offset);

        var latestStartLocal = new DateTimeOffset(
            date.Year, date.Month, date.Day,
            GetLatestStartTime().Hour, GetLatestStartTime().Minute, 0,
            date.Offset);

        var dayStartUtc = dayStartLocal.ToUniversalTime();
        var dayCloseUtc = dayCloseLocal.ToUniversalTime();
        var latestStartUtc = latestStartLocal.ToUniversalTime();

        // Hard daily limit based on local bookings only.
        var localBookings = (await _repository.GetByDateRangeAsync(dayStartUtc, dayCloseUtc, cancellationToken)).ToList();
        if (localBookings.Count >= 7)
        {
            _logger.LogInformation(
                "Day {Date} already has {Count} local bookings (limit: 7). Returning empty slots.",
                requestedDateInPoland.Date, localBookings.Count);
            return [];
        }

        var googleBusyPeriods = await _googleCalendarService.GetBusyPeriodsAsync(dayStartUtc, dayCloseUtc, cancellationToken);

        var busyPeriods = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var booking in localBookings)
        {
            if (string.IsNullOrEmpty(booking.GoogleCalendarEventId))
            {
                busyPeriods.Add((booking.StartTime, booking.EndTime));
            }
        }

        busyPeriods.AddRange(googleBusyPeriods);

        // Keep only periods intersecting the day and clamp them to [08:00, 23:00].
        var mergedBusyPeriods = MergeBusyPeriods(
            busyPeriods
                .Where(p => p.End > dayStartUtc && p.Start < dayCloseUtc)
                .Select(p => (
                    Start: p.Start < dayStartUtc ? dayStartUtc : p.Start,
                    End: p.End > dayCloseUtc ? dayCloseUtc : p.End))
                .ToList());

        var slots = new List<TimeSlotDto>();

        for (var currentSlotStartUtc = dayStartUtc;
             currentSlotStartUtc <= latestStartUtc;
             currentSlotStartUtc = currentSlotStartUtc.AddMinutes(30))
        {
            // Candidate cannot start inside an existing busy period.
            if (mergedBusyPeriods.Any(p => currentSlotStartUtc >= p.Start && currentSlotStartUtc < p.End))
            {
                continue;
            }

            // Gap before: previous busy end -> current start (or 08:00 -> current start).
            var previousBoundaryUtc = dayStartUtc;
            foreach (var period in mergedBusyPeriods)
            {
                if (period.End <= currentSlotStartUtc && period.End > previousBoundaryUtc)
                {
                    previousBoundaryUtc = period.End;
                }
            }

            var gapBeforeMinutes = (int)(currentSlotStartUtc - previousBoundaryUtc).TotalMinutes;
            if (!IsGapFillable(gapBeforeMinutes))
            {
                continue;
            }

            // Space ahead: current start -> next busy start (or 23:00).
            var nextBoundaryUtc = dayCloseUtc;
            foreach (var period in mergedBusyPeriods)
            {
                if (period.Start > currentSlotStartUtc && period.Start < nextBoundaryUtc)
                {
                    nextBoundaryUtc = period.Start;
                }
            }

            var spaceAheadMinutes = (int)(nextBoundaryUtc - currentSlotStartUtc).TotalMinutes;

            bool fits120 = spaceAheadMinutes >= Group2TotalBlockedDurationMinutes &&
                           IsGapFillable(spaceAheadMinutes - Group2TotalBlockedDurationMinutes);

            bool fits90 = spaceAheadMinutes >= Group1TotalBlockedDurationMinutes &&
                          IsGapFillable(spaceAheadMinutes - Group1TotalBlockedDurationMinutes);

            if (fits120)
            {
                slots.Add(new TimeSlotDto(currentSlotStartUtc.ToOffset(date.Offset), Group2TotalBlockedDurationMinutes));
            }
            else if (fits90)
            {
                slots.Add(new TimeSlotDto(currentSlotStartUtc.ToOffset(date.Offset), Group1TotalBlockedDurationMinutes));
            }
        }

        return slots
            .DistinctBy(x => x.StartTime)
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
            slot.MaxAvailableDurationMinutes >= requestedTotalBlockedDuration);

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

        var totalBlockedDurationMinutes = requestedTotalBlockedDuration;

        // Ensure StartTime is converted to UTC to satisfy Npgsql
        var booking = new Booking(
            customerInfo,
            dto.ParticipantsCount,
            dto.Package,
            dto.StartTime.ToUniversalTime(),
            totalBlockedDurationMinutes);

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


    private static bool IsGapFillable(int minutes)
    {
        if (minutes == 0) return true;
        if (minutes < 90) return false;
        if (minutes == 150) return false;
        return minutes % 30 == 0;
    }
}