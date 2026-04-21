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
            .Where(g => g.IsInternal && g.DurationMinutes <= 240)
            .ToList();

        bool priorityMode = priorityGaps.Count > 0;
        var targetGaps = priorityMode ? priorityGaps : gaps;

        // Key = UTC start, Value = max duration (90/120)
        var slotCandidates = new Dictionary<DateTimeOffset, int>();

        // 1) Magnetic slots (always)
        foreach (var gap in targetGaps)
        {
            AddMagneticCandidatesForGap(gap, latestStartUtc, slotCandidates);
        }

        // 2) Anchor grid only outside priority mode
        if (!priorityMode)
        {
            foreach (var hour in AnchorGridHours)
            {
                var anchorLocal = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, date.Offset);
                var anchorUtc = anchorLocal.ToUniversalTime();

                if (anchorUtc < dayStartUtc || anchorUtc > latestStartUtc)
                {
                    continue;
                }

                var containingGap = gaps.FirstOrDefault(g => anchorUtc >= g.Start && anchorUtc < g.End);
                if (containingGap.DurationMinutes == 0)
                {
                    continue;
                }

                var maxDuration = EvaluateCandidateInGap(anchorUtc, containingGap, mustBeMagnetic: false);
                if (maxDuration > 0)
                {
                    AddOrUpdateCandidate(slotCandidates, anchorUtc, maxDuration);
                }
            }
        }

        var finalSlots = slotCandidates
            .OrderBy(x => x.Key)
            .Select(x => new TimeSlotDto(x.Key.ToOffset(date.Offset), x.Value))
            .ToList();

        _logger.LogInformation(
            "Found {Count} available slots for {Date}. PriorityMode={PriorityMode}",
            finalSlots.Count, requestedDateInPoland.Date, priorityMode);

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

    private static void AddMagneticCandidatesForGap(
        Gap gap,
        DateTimeOffset latestStartUtc,
        Dictionary<DateTimeOffset, int> slotCandidates)
    {
        if (gap.DurationMinutes < Group1TotalBlockedDurationMinutes)
        {
            return;
        }

        // Candidate touching the left edge.
        var leftCandidate = gap.Start;
        if (leftCandidate <= latestStartUtc)
        {
            var maxLeft = EvaluateCandidateInGap(leftCandidate, gap, mustBeMagnetic: true);
            if (maxLeft > 0)
            {
                AddOrUpdateCandidate(slotCandidates, leftCandidate, maxLeft);
            }
        }

        // Candidates touching the right edge.
        var rightCandidate120 = gap.End.AddMinutes(-Group2TotalBlockedDurationMinutes);
        if (rightCandidate120 >= gap.Start && rightCandidate120 <= latestStartUtc)
        {
            var maxRight120 = EvaluateCandidateInGap(rightCandidate120, gap, mustBeMagnetic: true);
            if (maxRight120 > 0)
            {
                AddOrUpdateCandidate(slotCandidates, rightCandidate120, maxRight120);
            }
        }

        var rightCandidate90 = gap.End.AddMinutes(-Group1TotalBlockedDurationMinutes);
        if (rightCandidate90 >= gap.Start && rightCandidate90 <= latestStartUtc)
        {
            var maxRight90 = EvaluateCandidateInGap(rightCandidate90, gap, mustBeMagnetic: true);
            if (maxRight90 > 0)
            {
                AddOrUpdateCandidate(slotCandidates, rightCandidate90, maxRight90);
            }
        }
    }

    private static int EvaluateCandidateInGap(DateTimeOffset candidateStartUtc, Gap gap, bool mustBeMagnetic)
    {
        if (candidateStartUtc < gap.Start || candidateStartUtc >= gap.End)
        {
            return 0;
        }

        bool canFit120 = candidateStartUtc.AddMinutes(Group2TotalBlockedDurationMinutes) <= gap.End;
        bool canFit90 = candidateStartUtc.AddMinutes(Group1TotalBlockedDurationMinutes) <= gap.End;

        if (!canFit120 && !canFit90)
        {
            return 0;
        }

        int gapBefore = (int)(candidateStartUtc - gap.Start).TotalMinutes;
        if (!IsGapFillable(gapBefore))
        {
            return 0;
        }

        if (canFit120)
        {
            var end120 = candidateStartUtc.AddMinutes(Group2TotalBlockedDurationMinutes);
            int gapAfter120 = (int)(gap.End - end120).TotalMinutes;

            bool touchesEdge120 = candidateStartUtc == gap.Start || end120 == gap.End;
            if ((!mustBeMagnetic || touchesEdge120) && IsGapFillable(gapAfter120))
            {
                return Group2TotalBlockedDurationMinutes;
            }
        }

        if (canFit90)
        {
            var end90 = candidateStartUtc.AddMinutes(Group1TotalBlockedDurationMinutes);
            int gapAfter90 = (int)(gap.End - end90).TotalMinutes;

            bool touchesEdge90 = candidateStartUtc == gap.Start || end90 == gap.End;
            if ((!mustBeMagnetic || touchesEdge90) && IsGapFillable(gapAfter90))
            {
                return Group1TotalBlockedDurationMinutes;
            }
        }

        return 0;
    }

    private static void AddOrUpdateCandidate(Dictionary<DateTimeOffset, int> slotCandidates, DateTimeOffset startUtc, int duration)
    {
        if (slotCandidates.TryGetValue(startUtc, out var existing))
        {
            if (duration > existing)
            {
                slotCandidates[startUtc] = duration;
            }

            return;
        }

        slotCandidates[startUtc] = duration;
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