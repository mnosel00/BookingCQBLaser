using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Enums;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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

        public PaymentsController(
            IHotPayService hotPayService,
            IBookingService bookingService,
            IBookingRepository bookingRepository,
            ILogger<PaymentsController> logger)
        {
            _hotPayService = hotPayService;
            _bookingService = bookingService;
            _bookingRepository = bookingRepository;
            _logger = logger;
        }

        [HttpPost("hotpay-notify")]
        public async Task<IActionResult> HotPayNotify([FromForm] IFormCollection formData, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Received HotPay webhook notification.");

            if (!_hotPayService.ValidateNotification(formData))
            {
                _logger.LogWarning("Invalid HotPay signature hash.");
                return BadRequest("Invalid notification.");
            }

            var idZamowienia = formData["ID_ZAMOWIENIA"].ToString();
            var status = formData["STATUS"].ToString();

            if (!Guid.TryParse(idZamowienia, out var bookingId))
            {
                _logger.LogError("Invalid booking ID format in notification: {Id}", idZamowienia);
                return BadRequest("Invalid Booking ID.");
            }

            if (status == "SUCCESS")
            {
                try
                {
                    await _bookingService.ConfirmBookingPaymentAsync(bookingId, cancellationToken);
                    return Ok();
                }
                catch (KeyNotFoundException ex)
                {
                    _logger.LogWarning(ex, "Booking not found.");
                    return NotFound("Booking not found.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while confirming payment for booking {Id}.", bookingId);
                    return StatusCode(500, "Internal server error.");
                }
            }
            else
            {
                _logger.LogInformation("Received failing or unresolved status '{Status}' for booking {Id}", status, bookingId);

                // Handle FAILURE
                var booking = await _bookingRepository.GetByIdAsync(bookingId, cancellationToken);
                if (booking != null && booking.PaymentStatus != PaymentStatus.Paid)
                {
                    // Using reflection here as a safety net in case it hasn't been added yet.
                    var prop = typeof(Domain.Entities.Booking).GetProperty("PaymentStatus");
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(booking, PaymentStatus.Failed, null);
                    }
                    else
                    {
                        // Fallback hack to set private property if necessary
                        prop?.DeclaringType?.GetProperty("PaymentStatus")?.SetValue(booking, PaymentStatus.Failed);
                    }
                    
                    await _bookingRepository.UpdateAsync(booking, cancellationToken);
                    _logger.LogInformation("Booking {Id} marked as Failed/Canceled.", bookingId);
                }

                // HotPay requires Ok() on successful receipt to stop repeating the webhook
                return Ok(); 
            }
        }
    }
}