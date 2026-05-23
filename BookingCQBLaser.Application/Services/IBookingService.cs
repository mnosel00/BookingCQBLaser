using BookingCQBLaser.Application.DTOs;
using BookingCQBLaser.Domain.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Application.Services
{
    public interface IBookingService
    {
        Task<IEnumerable<TimeSlotDto>> GetAvailableTimeSlotsAsync(DateTimeOffset date, PackageType package, CancellationToken cancellationToken = default);
        Task<CreateBookingResponseDto> CreateBookingAsync(CreateBookingDto dto, CancellationToken cancellationToken = default);
        Task ConfirmBookingPaymentAsync(Guid bookingId, CancellationToken cancellationToken = default);
        Task ProcessPaymentWebhookAsync(Guid bookingId, string status, CancellationToken cancellationToken = default);
    }
}
