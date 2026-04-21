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
            ArenaOpenTime.Hour, ArenaOpenTime.Minute, 0,
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

        // 1) Fetch local bookings first (hard limit rule)
        var localBookings = (await _repository.GetByDateRangeAsync(dayStartUtc, dayCloseUtc, cancellationToken)).ToList();
        if (localBookings.Count >= 7)
        {
            _logger.LogInformation("Day {Date} already has {Count} local bookings (limit: 7). Returning empty slots.", requestedDateInPoland.Date, localBookings.Count);
            return [];
        }

        // 2) Build merged busy periods (local + Google)
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
        var mergedBusyPeriods = MergeBusyPeriods(busyPeriods);

        // 3) Build candidate starts using magnetic rules
        var candidateStarts = BuildCandidateStarts(
            date,
            dayStartUtc,
            latestStartUtc,
            mergedBusyPeriods);

        // 4) Evaluate each candidate: compute max gap to next busy or 23:00
        var results = new List<TimeSlotDto>();

        foreach (var candidateStartUtc in candidateStarts.OrderBy(x => x))
        {
            if (IsInsideBusyPeriod(candidateStartUtc, mergedBusyPeriods))
            {
                continue;
            }

            var nextBusyStartUtc = mergedBusyPeriods
                .Where(p => p.Start > candidateStartUtc)
                .Select(p => p.Start)
                .DefaultIfEmpty(dayCloseUtc)
                .Min();

            var boundaryUtc = nextBusyStartUtc < dayCloseUtc ? nextBusyStartUtc : dayCloseUtc;
            var availableGapMinutes = (int)(boundaryUtc - candidateStartUtc).TotalMinutes;

            int maxAvailableDurationMinutes =
                availableGapMinutes >= Group2TotalBlockedDurationMinutes ? Group2TotalBlockedDurationMinutes :
                availableGapMinutes >= Group1TotalBlockedDurationMinutes ? Group1TotalBlockedDurationMinutes :
                0;

            if (maxAvailableDurationMinutes == 0)
            {
                continue;
            }

            results.Add(new TimeSlotDto(
                candidateStartUtc.ToOffset(date.Offset),
                maxAvailableDurationMinutes));
        }

        var finalSlots = results
            .DistinctBy(x => x.StartTime)
            .OrderBy(x => x.StartTime)
            .ToList();

        _logger.LogInformation(
            "Found {Count} magnetic slots for {Date}",
            finalSlots.Count, requestedDateInPoland.Date);

        return finalSlots;
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

    private static HashSet<DateTimeOffset> BuildCandidateStarts(
        DateTimeOffset requestedDate,
        DateTimeOffset dayStartUtc,
        DateTimeOffset latestStartUtc,
        List<(DateTimeOffset Start, DateTimeOffset End)> mergedBusyPeriods)
    {
        var candidates = new HashSet<DateTimeOffset>();

        if (mergedBusyPeriods.Count == 0)
        {
            foreach (var hour in AnchorGridHours)
            {
                var anchorLocal = new DateTimeOffset(
                    requestedDate.Year, requestedDate.Month, requestedDate.Day,
                    hour, 0, 0,
                    requestedDate.Offset);

                candidates.Add(anchorLocal.ToUniversalTime());
            }
        }

        foreach (var busy in mergedBusyPeriods)
        {
            candidates.Add(busy.End);
            candidates.Add(busy.Start.AddMinutes(-Group1TotalBlockedDurationMinutes));
            candidates.Add(busy.Start.AddMinutes(-Group2TotalBlockedDurationMinutes));
        }

        candidates.RemoveWhere(c => c < dayStartUtc || c > latestStartUtc);

        return candidates;
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

    private static bool IsInsideBusyPeriod(
        DateTimeOffset instant,
        List<(DateTimeOffset Start, DateTimeOffset End)> busyPeriods)
    {
        foreach (var (busyStart, busyEnd) in busyPeriods)
        {
            if (instant >= busyStart && instant < busyEnd)
            {
                return true;
            }
        }

        return false;
    }
}