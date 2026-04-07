using BookingCQBLaser.Domain.Entities;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BookingCQBLaser.Domain.Interfaces
{
    public interface IHotPayService
    {
        string GeneratePaymentUrl(Booking booking, int amount);
        bool ValidateNotification(IFormCollection formData);
    }
}
