using BookingCQBLaser.Application.Services;
using BookingCQBLaser.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
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
        public async Task<IActionResult> HotPayNotify([FromForm] IFormCollection formData, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Received HotPay webhook notification.");

            if (!_hotPayService.ValidateNotification(formData))
            {
                _logger.LogWarning("Invalid HotPay hash or non-success status.");
                return BadRequest("Invalid notification.");
            }

            if (!Guid.TryParse(formData["ID_ZAMOWIENIA"], out var bookingId))
            {
                _logger.LogError("Invalid booking ID format in notification: {Id}", formData["ID_ZAMOWIENIA"]);
                return BadRequest("Invalid Booking ID.");
            }

            try
            {
                await _bookingService.ConfirmBookingPaymentAsync(bookingId, cancellationToken);
                return Ok("SUCCESS");
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
    }
}