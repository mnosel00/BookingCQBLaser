using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Application.DTOs
{
    public record CreateBookingResponseDto(Guid BookingId, string PaymentUrl);


}
