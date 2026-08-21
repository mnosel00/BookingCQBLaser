using System;

namespace BookingCQBLaser.Application.DTOs
{
    public record BookingStatusDto(Guid BookingId, string PaymentStatus);
}
