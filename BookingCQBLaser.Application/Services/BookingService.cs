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
    // See CONTEXT.md: Deposit is flat regardless of package/participants; Service Fee is the
    // online-payment surcharge that never reduces the on-site Remaining Balance.
    private const int Deposit = 300;
    private const int ServiceFee = 4;
    private const int OnlineChargeAmount = Deposit + ServiceFee;

    private readonly IBookingRepository _repository;
    private readonly IAvailabilityCalculator _availabilityCalculator;
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IEmailService _emailService;
    private readonly IHotPayService _hotpayService;
    private readonly ILogger<BookingService> _logger;

    public BookingService(
        IBookingRepository repository,
        IAvailabilityCalculator availabilityCalculator,
        IGoogleCalendarService googleCalendarService,
        IEmailService emailService,
        IHotPayService hotpayService,
        ILogger<BookingService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _availabilityCalculator = availabilityCalculator ?? throw new ArgumentNullException(nameof(availabilityCalculator));
        _googleCalendarService = googleCalendarService ?? throw new ArgumentNullException(nameof(googleCalendarService));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _hotpayService = hotpayService ?? throw new ArgumentNullException(nameof(hotpayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IEnumerable<TimeSlotDto>> GetAvailableTimeSlotsAsync(
         DateTimeOffset date,
         PackageType package,
         CancellationToken cancellationToken = default)
        => _availabilityCalculator.GetAvailableTimeSlotsAsync(date, package, cancellationToken);

    public async Task<CreateBookingResponseDto> CreateBookingAsync(
       CreateBookingDto dto,
       CancellationToken cancellationToken = default)
    {
        if (!_availabilityCalculator.IsOnlineBookingAllowed(dto.StartTime))
        {
            throw new InvalidOperationException("Rezerwacja online w tym dniu jest wyłączona, prosimy o kontakt telefoniczny lub SMS: 509 595 199");
        }

        var availableSlots = await _availabilityCalculator.GetAvailableTimeSlotsAsync(dto.StartTime, dto.Package, cancellationToken);
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
            totalBlockedDurationMinutes,
            dto.IsAdultGroup,
            dto.AgeRange);

        await _repository.AddAsync(booking, cancellationToken);
        _logger.LogInformation(
            "Booking {BookingId} saved to database as Pending. IsAdultGroup={IsAdultGroup}, AgeRange={AgeRange}",
            booking.Id,
            booking.IsAdultGroup,
            booking.AgeRange);

        string paymentUrl = _hotpayService.GeneratePaymentUrl(booking, OnlineChargeAmount);

        return new CreateBookingResponseDto(booking.Id, paymentUrl);
    }

    public async Task<BookingStatusDto> GetBookingStatusAsync(Guid bookingId, CancellationToken cancellationToken = default)
    {
        var booking = await _repository.GetByIdAsync(bookingId, cancellationToken);

        if (booking == null)
        {
            throw new KeyNotFoundException($"Booking with ID {bookingId} not found.");
        }

        return new BookingStatusDto(booking.Id, booking.PaymentStatus);
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
            int remainingBalance = Math.Max(0, totalCost - Deposit);

            await _emailService.SendBookingConfirmationAsync(booking, totalCost, Deposit, remainingBalance);
            _logger.LogInformation("Confirmation email sent to {Email} for booking {BookingId}", booking.Customer.Email, booking.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send confirmation email to {Email} for paid booking {BookingId}.", booking.Customer.Email, booking.Id);
        }
    }


    public async Task ProcessPaymentWebhookAsync(Guid bookingId, string status, decimal? reportedAmount, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Processing HotPay webhook for booking {BookingId} with status: {Status}, reported amount: {ReportedAmount}",
            bookingId, status, reportedAmount);

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

            // SECURITY: a validly-signed notification only proves it came from HotPay, not that
            // it paid the right amount. Reject anything that doesn't match what we charged.
            if (reportedAmount is null || Math.Round(reportedAmount.Value) != OnlineChargeAmount)
            {
                _logger.LogError(
                    "HotPay SUCCESS webhook for booking {BookingId} reported amount {ReportedAmount}, " +
                    "expected {ExpectedAmount}. Refusing to mark as paid.",
                    bookingId, reportedAmount, OnlineChargeAmount);
                throw new InvalidOperationException(
                    $"Reported payment amount for booking {bookingId} does not match the expected amount.");
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
}
