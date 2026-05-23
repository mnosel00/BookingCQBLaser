using BookingCQBLaser.Api.Filters;
using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using BookingCQBLaser.Infrastructure.ExternalServices.PGateway;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BookingCQBLaser.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly IHotPayService _hotPayService;
        private readonly IBookingService _bookingService;
        private readonly IBookingRepository _bookingRepository;
        private readonly ILogger<PaymentsController> _logger;
        private readonly HotPayOptions _hotPayOptions;


        public PaymentsController(
            IHotPayService hotPayService,
            IBookingService bookingService,
            IBookingRepository bookingRepository,
            ILogger<PaymentsController> logger,
            IOptions<HotPayOptions> hotPayOptions)
        {
            _hotPayService = hotPayService;
            _bookingService = bookingService;
            _bookingRepository = bookingRepository;
            _logger = logger;
            _hotPayOptions = hotPayOptions.Value;
        }

        [HttpPost("hotpay-notify")]
        public async Task<IActionResult> HotPayNotify([FromForm] IFormCollection formData, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Received HotPay webhook notification.");

            // ========== STEP 1: IP WHITELISTING VALIDATION ==========
            // HotPay explicitly requires verification of incoming webhook IP addresses
            var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            if (!ValidateRemoteIpAddress(remoteIp))
            {
                _logger.LogWarning(
                    "HotPay webhook REJECTED: Request from untrusted IP address '{RemoteIp}'. " +
                    "Returning HTTP 403 Forbidden per HotPay security requirements.",
                    remoteIp ?? "UNKNOWN");
                return Forbid();
            }

            _logger.LogDebug("IP validation passed for remote address: {RemoteIp}", remoteIp);

            // ========== STEP 2: SIGNATURE VALIDATION ==========
            // Validates SEKRET and SHA256 hash according to HotPay formula
            if (!_hotPayService.ValidateNotification(formData))
            {
                _logger.LogWarning("HotPay webhook REJECTED: Invalid signature hash or SEKRET mismatch.");
                return BadRequest("Invalid notification signature.");
            }

            _logger.LogDebug("Signature validation passed.");

            // ========== STEP 3: EXTRACT AND VALIDATE PARAMETERS ==========
            var idZamowienia = formData["ID_ZAMOWIENIA"].ToString();
            var status = formData["STATUS"].ToString();

            if (!Guid.TryParse(idZamowienia, out var bookingId))
            {
                _logger.LogError("Invalid booking ID format in HotPay notification: {Id}", idZamowienia);
                return BadRequest("Invalid Booking ID format.");
            }

            // ========== STEP 4: FETCH BOOKING FROM REPOSITORY ==========
            var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
            if (booking == null)
            {
                _logger.LogWarning("Booking not found for HotPay notification with ID: {BookingId}", bookingId);
                return NotFound("Booking not found.");
            }

            // ========== STEP 5: HANDLE SUCCESS STATUS ==========
            if (status == "SUCCESS")
            {
                // IDEMPOTENCY CHECK: If already marked as Paid, skip re-processing
                if (booking.PaymentStatus == PaymentStatus.Paid)
                {
                    _logger.LogInformation(
                        "Received duplicate SUCCESS notification for already-paid booking {BookingId}. " +
                        "Returning Ok() without re-processing (idempotent behavior).",
                        bookingId);
                    return Ok();
                }

                try
                {
                    await _bookingService.ConfirmBookingPaymentAsync(bookingId, cancellationToken);
                    _logger.LogInformation("Booking {BookingId} payment confirmed successfully via HotPay.", bookingId);
                    return Ok();
                }
                catch (KeyNotFoundException ex)
                {
                    _logger.LogWarning(ex, "Booking not found while confirming payment for {BookingId}.", bookingId);
                    return NotFound("Booking not found.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error confirming payment for booking {BookingId}.", bookingId);
                    return StatusCode(500, "Internal server error.");
                }
            }

            // ========== STEP 6: HANDLE FAILURE/PENDING STATUS ==========
            else if (status == "FAILURE" || status == "PENDING")
            {
                // IDEMPOTENCY CHECK: If already in terminal state, don't re-process
                if (booking.PaymentStatus == PaymentStatus.Failed || booking.PaymentStatus == PaymentStatus.Paid)
                {
                    _logger.LogInformation(
                        "Received {Status} notification for booking {BookingId} already in {CurrentStatus} state. " +
                        "No state change needed.",
                        status,
                        bookingId,
                        booking.PaymentStatus);
                    return Ok();
                }

                try
                {
                    // Use domain method MarkAsFailed() - maintains encapsulation and DDD principles
                    // NO REFLECTION HACK - this is clean, maintainable code
                    booking.MarkAsFailed();
                    await _bookingRepository.UpdateAsync(booking, cancellationToken);
                    _logger.LogInformation(
                        "Booking {BookingId} marked as Failed due to HotPay status: {Status}.",
                        bookingId,
                        status);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Cannot mark booking {BookingId} as failed (current PaymentStatus: {CurrentStatus}). " +
                        "Error: {ErrorMessage}",
                        bookingId,
                        booking.PaymentStatus,
                        ex.Message);
                    // Return Ok to stop HotPay from retrying
                    return Ok();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error marking booking {BookingId} as failed.", bookingId);
                    // Return Ok to prevent HotPay retry loop; error will be in application logs
                    return Ok();
                }

                return Ok();
            }

            // ========== STEP 7: UNKNOWN STATUS ==========
            else
            {
                _logger.LogWarning(
                    "Received unknown HotPay status '{Status}' for booking {BookingId}. Ignoring.",
                    status,
                    bookingId);
                // Return Ok to prevent retry loop on unknown statuses
                return Ok();
            }
        }

        private bool ValidateRemoteIpAddress(string? remoteIp)
        {
            if (string.IsNullOrEmpty(remoteIp))
            {
                _logger.LogWarning("Remote IP address is null or empty. Rejecting webhook.");
                return false;
            }

            var trustedIps = _hotPayOptions.TrustedWebhookIpAddresses
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (trustedIps.Count == 0)
            {
                _logger.LogWarning(
                    "No trusted HotPay IP addresses configured in HotPayOptions.TrustedWebhookIpAddresses. " +
                    "Rejecting all webhook requests.");
                return false;
            }

            var isWhitelisted = trustedIps.Contains(remoteIp);

            if (!isWhitelisted)
            {
                _logger.LogWarning(
                    "IP address '{RemoteIp}' not in HotPay whitelist. " +
                    "Trusted HotPay IPs: {TrustedIps}",
                    remoteIp,
                    string.Join(", ", trustedIps));
            }

            return isWhitelisted;
        }
    }
}