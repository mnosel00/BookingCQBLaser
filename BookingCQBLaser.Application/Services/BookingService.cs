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
    private const int SlotIntervalMinutes = 30;
    private const int MinimumSlotCapacityMinutes = 90;

    private static readonly TimeOnly ArenaOpenTime = new(9, 0);
    private static readonly TimeOnly ArenaCloseTime = new(23, 0);

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
         PackageType package,
         CancellationToken cancellationToken = default)
    {
        var polandTimeZone = GetPolandTimeZone();
        var nowInPoland = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, polandTimeZone);

        var requestedDateInPoland = TimeZoneInfo.ConvertTimeFromUtc(date.UtcDateTime, polandTimeZone);

        if (!IsOnlineBookingAllowed(requestedDateInPoland, nowInPoland))
        {
            _logger.LogInformation("Requested date {Date} is not allowed by booking rules. Returning empty slots.", requestedDateInPoland.Date);
            return new List<TimeSlotDto>();
        }

        var packageBaseDuration = package.GetBaseDurationMinutes();
        var clientGameDuration = package.GetClientGameDurationMinutes();

        var dateOnlyInPoland = requestedDateInPoland.Date;
        var polandOffset = polandTimeZone.GetUtcOffset(dateOnlyInPoland);

        // Create day boundaries with Poland offset
        var dayStart = new DateTimeOffset(
            dateOnlyInPoland.Year, dateOnlyInPoland.Month, dateOnlyInPoland.Day,
            ArenaOpenTime.Hour, ArenaOpenTime.Minute, 0,
            polandOffset);

        var dayEnd = new DateTimeOffset(
            dateOnlyInPoland.Year, dateOnlyInPoland.Month, dateOnlyInPoland.Day,
            ArenaCloseTime.Hour, ArenaCloseTime.Minute, 0,
            polandOffset);


        // Convert boundaries to UTC for DB and Google Calendar queries
        var busyPeriods = await GetCombinedBusyPeriodsAsync(
            dayStart.ToUniversalTime(),
            dayEnd.ToUniversalTime(),
            cancellationToken);

        // Get free windows between busy periods
        var freeWindows = GetFreeWindows(dayStart, dayEnd, busyPeriods);

        // Generate slots for each free window
        var availableSlots = new List<TimeSlotDto>();
        foreach (var (windowStart, windowEnd) in freeWindows)
        {
            var windowSlots = GenerateSlotsForWindow(windowStart, windowEnd, packageBaseDuration, clientGameDuration);
            availableSlots.AddRange(windowSlots);
        }

        _logger.LogInformation(
            "Found {Count} available slots for {Date} with package {Package} across {WindowCount} free windows",
            availableSlots.Count, date.Date, package, freeWindows.Count);

        return availableSlots;
    }

    private List<(DateTimeOffset Start, DateTimeOffset End)> GetFreeWindows(
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

    private List<TimeSlotDto> GenerateSlotsForWindow(
    DateTimeOffset windowStart,
    DateTimeOffset windowEnd,
    int packageBaseDuration,
    int clientGameDuration)
    {
        var slots = new List<TimeSlotDto>();
        int windowDurationMinutes = (int)(windowEnd - windowStart).TotalMinutes;

        if (windowDurationMinutes < MinimumSlotCapacityMinutes) // 90 min
        {
            return slots;
        }

        var currentSlotStart = windowStart;

        
        var latestStartFoundByMinimum = windowEnd.AddMinutes(-MinimumSlotCapacityMinutes);

        while (currentSlotStart <= latestStartFoundByMinimum)
        {
            int maxAvailableDuration = (int)(windowEnd - currentSlotStart).TotalMinutes;

            
            int gapBefore = (int)(currentSlotStart - windowStart).TotalMinutes;

           
            bool isGapBeforeValid = gapBefore == 0 || gapBefore >= MinimumSlotCapacityMinutes;

            if (isGapBeforeValid)
            {
                
                var displaySlotEnd = currentSlotStart.AddMinutes(clientGameDuration);

                slots.Add(new TimeSlotDto(currentSlotStart, displaySlotEnd, maxAvailableDuration));
            }

            currentSlotStart = currentSlotStart.AddMinutes(SlotIntervalMinutes);
        }

        return slots;
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

        var totalBlockedDurationMinutes = dto.Package.GetBaseDurationMinutes();

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
            _logger.LogInformation("Booking {Id} was already process as paid.", booking.Id);
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

 
    public async Task ProcessPaymentWebhookAsync(Guid bookingId, string status, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing HotPay webhook for booking {BookingId} with status: {Status}",
            bookingId, status);

        // ===== STEP 1: FETCH BOOKING =====
        var booking = await _repository.GetByIdAsync(bookingId, cancellationToken);
        
        if (booking == null)
        {
            _logger.LogError("Booking not found for webhook processing: {BookingId}", bookingId);
            throw new KeyNotFoundException($"Booking with ID {bookingId} not found.");
        }

        // ===== STEP 2: HANDLE SUCCESS STATUS =====
        if (status == "SUCCESS")
        {
            // IDEMPOTENCY: If already paid, skip processing
            if (booking.PaymentStatus == PaymentStatus.Paid)
            {
                _logger.LogInformation(
                    "Received duplicate SUCCESS webhook for already-paid booking {BookingId}. " +
                    "Idempotent: skipping re-processing.",
                    bookingId);
                return;
            }

            // Delegate to existing payment confirmation flow
            await ConfirmBookingPaymentAsync(bookingId, cancellationToken);
        }

        // ===== STEP 3: HANDLE FAILURE/PENDING STATUS =====
        else if (status == "FAILURE" || status == "PENDING")
        {
            // IDEMPOTENCY: If already in terminal state, skip processing
            if (booking.PaymentStatus == PaymentStatus.Failed || booking.PaymentStatus == PaymentStatus.Paid)
            {
                _logger.LogInformation(
                    "Received {Status} webhook for booking {BookingId} already in {CurrentStatus} state. " +
                    "Idempotent: no state change needed.",
                    status,
                    bookingId,
                    booking.PaymentStatus);
                return;
            }

            try
            {
                // Use domain method - maintains encapsulation and DDD principles
                booking.MarkAsFailed();
                await _repository.UpdateAsync(booking, cancellationToken);
                
                _logger.LogInformation(
                    "Booking {BookingId} marked as Failed due to HotPay status: {Status}.",
                    bookingId,
                    status);
            }
            catch (InvalidOperationException ex)
            {
                // This can occur if booking is already in a terminal state
                _logger.LogWarning(
                    ex,
                    "Cannot mark booking {BookingId} as failed (current PaymentStatus: {CurrentStatus}): {Error}",
                    bookingId,
                    booking.PaymentStatus,
                    ex.Message);
                throw;
            }
        }

        // ===== STEP 4: UNKNOWN STATUS =====
        else
        {
            _logger.LogWarning(
                "Received unknown HotPay status '{Status}' for booking {BookingId}. Ignoring.",
                status,
                bookingId);
            // Don't throw - unknown statuses are logged but don't cause errors
        }
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