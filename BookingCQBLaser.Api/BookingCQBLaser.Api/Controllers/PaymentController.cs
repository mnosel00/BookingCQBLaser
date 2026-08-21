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
using System.Linq;
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
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            IHotPayService hotPayService,
            IBookingService bookingService,
            ILogger<PaymentsController> logger)
        {
            _hotPayService = hotPayService;
            _bookingService = bookingService;
            _logger = logger;
        }

        [HttpPost("hotpay-notify")]
        [ServiceFilter(typeof(HotPayIpWhitelistFilter))]
        public async Task<IActionResult> HotPayNotify([FromForm] IFormCollection formData, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Received HotPay webhook notification.");

            // Translate the ASP.NET-specific form payload into a plain dictionary at the API boundary,
            // so the Domain-level IHotPayService contract stays framework-agnostic.
            var notificationData = formData.Keys.ToDictionary(key => key, key => formData[key].ToString());

            // ===== STEP 1: VALIDATE WEBHOOK SIGNATURE =====
            if (!_hotPayService.ValidateNotification(notificationData))
            {
                _logger.LogWarning("HotPay webhook REJECTED: Invalid signature hash or SEKRET mismatch.");
                return BadRequest("Invalid notification signature.");
            }

            _logger.LogDebug("Signature validation passed.");

            // ===== STEP 2: PARSE PAYMENT PARAMETERS =====
            var idZamowienia = notificationData.GetValueOrDefault("ID_ZAMOWIENIA", string.Empty);
            var status = notificationData.GetValueOrDefault("STATUS", string.Empty);

            if (!Guid.TryParse(idZamowienia, out var bookingId))
            {
                _logger.LogError("Invalid booking ID format in HotPay notification: {Id}", idZamowienia);
                return BadRequest("Invalid Booking ID format.");
            }

            // ===== STEP 3: DELEGATE TO APPLICATION LAYER =====
            // All business logic, state management, and persistence is handled here
            try
            {
                await _bookingService.ProcessPaymentWebhookAsync(bookingId, status, cancellationToken);
                _logger.LogInformation("HotPay webhook processed successfully for booking {BookingId}.", bookingId);
                return Ok();
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Booking not found for HotPay webhook: {BookingId}", bookingId);
                return NotFound("Booking not found.");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "Invalid booking state for HotPay webhook: {BookingId}", bookingId);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing HotPay webhook for booking {BookingId}.", bookingId);
                return StatusCode(500, "Internal server error.");
            }
        }
    }
}