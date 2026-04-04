using BookingCQBLaser.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Interfaces
{
    public interface IEmailService
    {
        Task SendBookingConfirmationAsync(Booking booking, int totalCost, int depositAmount, int remainingBalance);
    }
}
